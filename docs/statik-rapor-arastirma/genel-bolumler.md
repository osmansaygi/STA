# GPR'siz "Yapı genel" bölümleri — veri kaynakları ve doğrulama (ara_genel)

Araç: `scratchpad\ara_genel\tool` (net10, bağımsız; `dotnet build -c Release -o bin`).
Kullanım: `bin\Ara.exe <bölüm> [GPR yolu | all]` → `cmp_<bölüm>_one|all.txt` (fark satırları).
Bölüm adları: `giris dos kir ops p7 p8 aks katmom nokta mod devrilme`. `REVMIN=2026` yalnız o yıl ve sonrası
STA4 revizyonuyla basılmış GPR'leri alır; `PAGES=n` okunan sayfa sayısı.
Doğrulama derlemi: D:\projeler altındaki 1114 GPR'den ST4'ü olan ~1017 proje (`gprs.txt`). Üreteç GPR okumaz;
GPR yalnız karşılaştırmada (tek istisna: s.7 "Rev.(…)" program tarihi, aşağıda).
Karşılaştırma: üretilen her satır GPR gövdesinde (sayfa başlıkları atılmış) sırayla aranır; "satır" = birebir aynı
satır, "sayı" = satırdaki sayı belirteçleri.

Sayı biçimi notları: `Fmt.N` = float32 → G7 → yarım yukarı; OPS/ST4'ten gelen ham alanlar çoğunlukla dosyadaki
metin aynen (".8", ".0025") basılır; "4 / 5,5" gibi değerler VB CStr Türkçe yerel (virgül).

---

## 1. s.1–2 Program giriş + PERDE ve KOLONLARDA EKSANTRISITE

- s.1 = `C:\Sta4V14\STA4.TXT` (1254) boş olmayan satırları, aynen. Projeye bağlı alan yok.
- s.2 (EKSANTRISITE + KAYMA DEFORMASYONU ve RIJITLIK BOLGELERI + DEGISKEN RIJIT BOLGE) sabit metin; hiçbir
  dosyada yok (OUTPRN.EXE içinde). 1029 GPR'de birebir aynı; 85 GPR'de bölüm basılmamış. Kendi cümlemizle yazılabilir.
- Doğrulama: s.1 2026 derlemi 243/243 proje, 11907/11907 satır; N_ULK_02, VKF_01, CP_03 s.1–2 birebir aynı.

## 2. s.3–4 DÖŞEME / KİRİŞ YÜK ANALİZİ

Kaynak: proje `.USE` (metin, 1254). .DWL/.SEE/.SGL değil.
- 1. satır "Load Version=3.0"; 2. satır kat bazlı hareketli yük azaltma % (s.8 tablosu); 3. satır `nd,nw,…` = döşeme
  ve duvar tipi sayısı; 6. satır PRE dosyası (PANELTON.PRE). 7. satırdan itibaren:
- Döşeme bloğu (nd adet, 4 satır): ad / kaplama adı / ilave adı / 9 sayı
  `γkap, tkap, tharç, ttesviye, tsıva, γdolgu, tdolgu, γilave, tilave`.
  Satırlar: Kaplama (γkap×tkap, ad 12 karaktere doldurulur), Kaplama harcı (2.200×tharç), Tesviye betonu (2.000×ttes),
  Sıva (2.200×tsıva), Dolgu (γdolgu×tdolgu), İlave (γilave×tilave). Kalınlığı 0 olan satır basılmaz; TOPLAM her zaman.
  Harç/tesviye/sıva yoğunlukları (2.2/2.0/2.2) programda sabit.
- Duvar bloğu (nw adet, 2 satır): ad / 10 sayı `tip, kalınlık(cm), birim ağırlık(t/m²), yükseklik, γpencere,
  hpencere, …`. Satır: "Duvar yükü (kalınlık cm)" w×h; γpencere≠0 ise "Pencere" satırı + TOPLAM.
- Sayılar 3 ondalık; 7 karaktere sığmazsa ondalık azaltılır (1800.00). Çarpım float32.
- Doğrulama (tüm derlem): döşeme 1003/1004 proje, 58044/58049 satır (kalan 1 = USE GPR'den sonra değişmiş);
  kiriş 1004/1004, 69384/69384. 2026: 242/242 her ikisi. N_ULK 61/61 + 69/69; VKF 62/62 + 69/69; CP_03 61/61 + 69/69.

## 3. s.5–6 GENEL / DÖŞEME / KİRİŞ / KOLON-PERDE / TEMEL BETONARME OPSİYONLARI

Kaynak: proje `.OPS` (metin; satır no 1 tabanlı, "Ver14.02" = 1. satır) + ST4 + iki hesap. Eşleme
(`tool\Opsiyon.cs`; ★ = derlemde değer değişiyor ve kural doğrulandı, diğerleri derlemde hep aynı değer,
alan konumu sıra/değer uyumuyla tahmin):
- GENEL: max demir boyu 17.1; bindirme ø×17.2; Lp Lnet1/17.3; Lpa /17.6; Lpu cm 17.4; Lpu d/17.5; Lk Lnet2/17.7;
  pilye kayma katılım 17.10; kanca ø×10 (sabit); aderans ø×17.8; sık etriye 17.11=1 "zorunlu"; pilye 17.12=1
  "pilyesiz"; Lnet/2, "düz", 2.5 sabit.
- DÖŞEME: paspayı 9.1★ (virgüllü); max aralık 9.6 + d×9.7; iki/tek yönlü ρmin 9.2/9.3; nervür "20, d/2" ve
  "50Ø, Ln/4" sabit (OPS alanı değil, derlemde 359/359 sabit).
- KİRİŞ: boyuna paspayı 10.1★, etriye paspayı = boyuna − 1.5★ (Ver.13.1'de −2); sehim L/10.2, L/10.3; min çekme
  ★ = 0.8·fctd/fyd, fctd = 0.35·√fck/γc, fyd = fy/γs (MPa), fck/fy = OPS 4./5. satır listesinden ST4 6. satır
  2. (kiriş) / 4. alan indeksleriyle, γc/γs = OPS 2.2/2.3, 4 ondalık "0.####"; çaplar 10.7★/10.8★/10.9★/10.10★;
  pilye açısı 11.6; gövdesiz h 11.7★; düz aralık 11.8; kayma katılım 11.9; süreklilik 10.5; montaj oranı 10.6;
  S max/min 11.1/11.2★; Sk1 11.3, d/11.4, ø×11.5; tek etriye 10.11★; alt/üst As 11.11; min.üst As satırı
  OPS 36.7≠0 ise "0,8 x fctd / fyd", =0 ise "Fctd/Fyd … Hayır"★ (990/1017).
- KOLON-PERDE: paspayı 12.1★ (etriye −1.5); ρ 12.2★/12.3; etriye aralık 12.5★/12.6, ø×12.7, çiroz ø×12.8;
  min çap 12.9★, etriye çapı 12.10★; D/B 12.11★ (başlıktaki "D >= 6 × B" dahil); gönye OPS 6.6=0 "Gönyesiz"
  aksi "Gönyeli"★ (1012/1017); Hcr ve başlık/gövde satırları 13.1…13.11 (13.6★); başlık/gövde çapı 12.14★/12.15★;
  tasarım eğilme momenti 13.12=1 "Evet".
- TEMEL: paspayı 14.1★; min çekme ★ aynı bağıntı, beton ST4 6.3, çelik 6.6; toplam 14.3; basınç oranı OPS 6.3;
  pilye 14.4; etriye 14.5/14.6; max genişlik 14.10★; düz aralık 14.7; filiz 14.8; Müt. temel çapları 15.3–15.7★;
  Ha 14.9★ (0 → "Düz ampatman").
- Sürüm farkı: "DOSEME BETONARME OPSIYONLARI" yalnız 2025+ revizyonda basılıyor (bu tarihten önce paspayı GENEL'de).
- Doğrulama: 2026 derlemi 240/242 proje, 24435/24442 satır, 27580/27585 sayı (kalan 2 projede OPS GPR'den sonra
  değişmiş/eski tip). N_ULK, VKF_01, CP_03: 101/101 satır.

## 4. s.7–9 STA4-CAD PROGRAMI, kombinasyonlar, azaltma, duvar, rüzgar, akslar, diyafram

`tool\Sayfa7.cs`, `Kombinasyon.cs`, `Aks.cs`.
- Başlık "Ver.14.1 Rev.(d.M.yyyy)": program revizyon tarihi; proje verisi değil. GPR'siz üretimde
  `C:\Sta4V14\OUTPRN.EXE` (aynı gün OUTSTA.EXE) dosya tarihi = 19.10.2026 (N_ULK GPR'si ile aynı). Karşılaştırmada GPR'deki tarih kullanıldı.
- ST4 başlık eşlemesi (satır.alan): proje adı 3. satır; kat adedi 4.1; kolon sayısı 4.2; X/Y aks sayısı 5.1/5.2;
  zemin sınıfı 14.6 (21 ZA, 22 ZB, 23 ZC, 24 ZD, 25 ZE; 985/985); koordinat 14.7/14.8 (ham metin); Ss/S1 14.4/14.5 F3;
  R 4.5 F2; D 14.1 ("0.###"); Ta/Tb = 0.2·Sd1/Sds, Sd1/Sds (14.2/14.3) F3; n 4.9 F1; sıfır rölatif kot 7.1 F2;
  Cz 7.6 F1; zemin taşıma 7.3 F1; yatak 7.2 F1; beton yoğunluğu /Materials/ 1. satır 4. alan; ısı farkı 10.2 F1;
  analiz 13.4 (1 FEA3D, 0 FRAME3D) ve mesh genişliği 13.5 ("#.###" → ".5"); temel analiz 8.3 (0 temelsiz,
  1 sadece dönme, 2 tüm deplasman); kolonun oturduğu kiriş: OPS 7.1=5 → "Düşey deprem analizi yapılmıştır.";
  diğer metinler (TBDY2018, TS500, BRÜT KESİT, MODAL, Yarı Sonsuz Rijit, Elastik ankastre) derlemde sabit.
- Zemin gerilmesi hareketli yük azaltma değeri = Σ_{i=1..N}(1 − e_i/100)/N, e = USE 2. satır, N = ST4 4.1
  (eski revizyonlarda tüm projeler birebir). 2026 revizyonunda 15 projede farklı (VKF_01 ve CP_03: GPR 0.67, bağıntı 0.59);
  yeni kural çözülemedi (ağırlıklı ortalama, bodrum hariç vb. denendi, tutmadı).
- Çatlamış kesit tablosu: TBDY Tablo 4.2'nin STA4 sabitleri; 994/997 GPR'de aynı, dosyada yok → sabit yazılır.
- Malzeme tablosu: satır sınıfı ST4 6. satır (1 plak/nervür, 1 HNP, 3 temel, 2 kiriş\kolon; çelik genel/etriye
  4/5, temel 6/7) → OPS 4. satır beton listesi (kg/cm²) ve 5. satır çelik listesi (1 tabanlı indeks).
  E = ⌊(3250·√fck[MPa] + 14000)/50⌋·50 ×10 (C25 302500, C30 318000, C35 332000, C40 345500), G = 0.4·E;
  birim ağırlık /Materials/ 1. satır 4. alan. HNP satırı: ST4 9.8≠0 iken hazır plak ürününden (C30/S500 görüldü,
  8 proje) — çözülmedi. Malzeme katsayıları OPS 2.2/2.3; yük katsayıları COD 1. kombinasyon.
- Kombinasyon tabloları (s.8): proje `.COD` varsa o, yoksa `C:\Sta4V14\STA4.COD` (= TS500T.COD); 1. satır adet,
  sonra satırlar (Cg Cq Cs Ce Cw Ct), ad `n+2`. satırda ("CODE:TS500T.COD"). Zemin gerilmesi tablosu aynı katsayılar.
  OPS 7.1=5 ise TBDY düşey deprem satırı. ST4 10.2≠0 (ısı) ise 3. sütun başlığı "T c°" (zemin tablosunda "Isı"),
  ayrıca "KAT ISI YÜK BİLGİLERİ" tablosu basılıyor (üretilmedi).
- Azaltma tablosu USE 2. satır (0 boş), Cz (ST4 7.6)=0 ise tablo yok. Duvar malzeme tablosu /Materials/ 2–6. satır
  (E, fbd, fbtd), yalnız ST4 11.10=1 iken (1017/1017). Rüzgar: /Wind parameters/ (emme, basınç, (H,Qw)…; 0 çiftler atlanır).
- Aks tablosu: /Axis data/ ilk nx satır X, sonra ny Y; Ax/Bx = 1./2. alan F2; isim `.AKS` dosyası (4 karakter/satır,
  aynı sıra). Kısa taraf bitince o tarafa alt çerçeve, sonra boşluk.
- 1. kat kolon aksları: /Column axis data/ satırları (X aks 1000+i, Y aks 2000+j; 2000'lik değer X alanında ise "nY");
  dx/dy = 4./5. alan /10 F1; alt yük 6. alan; kolon no taban (100/1000) /Columns Data/ en büyük no/100 ≥ hikâye sayısı.
- Kat diyaframları: /Story/ (su basman hariç) kat sayısı ≤1 ise yok; tepe etiketi en üst katın diyafram no'su,
  her bant altındaki katın diyafram no'su. Çok bloklu (bir katta birden çok diyafram) şema çözülmedi.
- Doğrulama 2026 derlemi: s.7 203/242 proje (14697/14762 satır; kalan: 15 azaltma değeri, 8 HNP, 5 performans
  şablonu); s.8 221/242 (kalan 21 = ısı tablosu); aks/kolon/diyafram 203/243 (15815/16369 satır; kalan çok bloklu
  diyafram + eski GPR). N_ULK s.7 61/61, s.8 62/62, s.9 53/53; VKF_01 60/61, 62/62, 65/65; CP_03 60/61, 62/62, 94/94.

## 5. s.11–13 Perde taban moment kontrolü, düzensizlikler

Çözülen:
- Kat deprem momenti tablosu (s.11 ve s.29 ortak): `.FeaSeismic` metin. 2. satır `K,Tx,Ty,R,Wx,Wy,Vtx,Vty,Ftx,Fty`,
  3. satır toplamlar, sonra K satır `Fmodal X, Fmodal Y, Feşd X, Feşd Y, Fdeprem X, Fdeprem Y` (1. kattan yukarı),
  sonra K satır `Wg, Wq`. Tablo F = Fdeprem (5./6. sütun), H = kat kotu − Hbodrum; Hbodrum = ST4 10. satır 6. alan
  (N_ULK 0, VKF 4, CP 4). Kot ≤ Hbodrum olan katlar "BODRUM / -" ve toplama girmez. Satırlar tepeden; F·H F2.
  Doğrulama: N_ULK 9/9, VKF 16/16, CP_03 19/19 satır; 2026 derlemi 173/202 proje (kalan çok diyaframlı/eski).
- Perde My (= My sütunu): COL kaydı +378 (alt uç My dizisi) indeks 10/11 (EY±) mutlak büyüğü: SZ-02 40.94, SZ-12 38.95 ✓.
  (Mx için +202 indeks 8/9 beklenir; N_ULK'da X perdesi yok, denetlenmedi.)
- èx, èy = Sa(T,DD3)/Sa(T,DD2): T = `.TP2` 1. satır (Tx,Ty), DD2 Sds/Sd1 = ST4 14.2/14.3, DD3 = 14.9/14.10 → 0.128/0.338=0.378,
  0.166/0.483=0.343 ✓. Max θ = 0.12·D/(Ch·R) (Ch=0.5) = 0.150 ✓. à = 0.5·R/I ✓.
- `.DRP` metin dosyası kontrol cümlelerinin kısa (ASCII) kopyasını içerir (A1/B2, 4.9.1.3, 4.9.2.2 sonuçları ve
  Cmx/Cmy) — üretimde doğrudan okunabilir; ancak Cmy=0.21 iken GPR 0.22 yazıyor.

Çözülemeyenler:
- ΣMk (perdeye bağlı kiriş deprem momentleri, 10.13/14.36): BEA SolM(+38)/SağM(+126) EY± uç momentlerinin
  perde düğümüne bağlanan kirişlerdeki toplamları (8.99–11.3 arası) tutmadı; yüz momenti ya da başka kombinasyon olabilir.
- A1/B2 tablosu (ΔX düst/dalt, ηbi, ηki, θ, minimum derz uiX): değerler DIS'teki CQC diyafram/düğüm
  yerdeğiştirmelerinden (rijit hesap 3.84 mm iken GPR 2.84 mm) ve DIS'in 15–20. ek vakalarından türetilemedi; TBDY'nin
  istediği eşdeğer deprem yükü + ±%5 dışmerkezlik analizinin sonucu olmalı ve hiçbir dosyada saklanmıyor (.EAR, .TBI, .SSM,
  .PDE, .CQC, .KNT tarandı; .KNT aks ölçülendirme, .SSM kat rijitlik matrisi metni, .EAR kat bilgisi+diyafram yerdeğiştirmesi+merkezler).
- B1 tablosu (Aw, Agx, Agy, Akx, Aky, ΣAe, ηci): geometriden hesaplanabilir görünüyor, kural araştırılmadı.

## 6. s.14 MODAL ANALİZ MOD GRAFİĞİ

Kaynak `.CQC` (metin): 1. satır mod sayısı n; n satır `T, baskın yön (1 X, 2 Y, 4 burulma), Mx%, My%, Mz%`;
n satır mod vektörü `Dx[K], Dy[K], Rz[K]` (K = `.EAR` 1. satır 1. alan = diyafram sayısı). X yönü: yönü 1 olan ilk
min(4, K) mod, her katın Dx·100 (F1, alttan üste); Y yönü aynı yönü 2, Dy. Grafik bu sayılarla çizilir.
Doğrulama: N_ULK 13/13, VKF_01 81/81, CP_03 105/105 sayı; 2026 derlemi 210/242 proje (kalan çok bloklu/eski).

## 7. s.15–28 NOKTA DEPLASMANLARI

Kaynaklar `.DIS` (float32), `.EAR`, `.SEE`, `.COL`, ST4.
- DIS: 21 vaka × (N+K) yuva × 3 float. N = `.EAR` 1. satır 2. alan (düğüm), K = 1. alan. Vaka sırası: 0–6 düşey
  düzenler, 7 zemin, 8/9 X±%5, 10/11 Y±%5, 12/13 WX/WY, 14 ısı, 15–20 ek deprem vakaları. Düğüm yuvası
  (n−1)·3: `[éy, éx, ëz]` (rad, rad, m) → tabloda ×1000. Son K yuva diyafram: `Dx[K], −Dy[K], Rz[K]`.
- ëx/ëy rijit diyafram: kat k merkezi (Xr, Yr) = `.EAR` merkez satırları (K kat satırı + "17,…" satırı + 2K + K satırdan
  sonra K satır `Xm, Xr, Ym, Yr`; 2./4. değer). Düğüm aks koordinatı (X, Y): kolon üst düğümü = COL kaydı +0 int16;
  kolon no %taban → /Column axis data/ satırı → X/Y aks koordinatı (Bx). Kolonsuz düğümler (kiriş-kiriş birleşimi):
  `.SEE` "//NOKTA KOORDINATLARI//" (no, X, Y, Z fiziksel) ile kat (Z = kat kotu) ve en yakın kolonsuz kiriş ucu
  (ST4 kiriş 2. satır 5./6. alan 0) aks kesişimi. ëx = Dx + Rz·(Y − Yr), ëy = Dy − Rz·(X − Xr) (double hesap,
  float'a çevir, ×1000f); temel düğümleri (SEE Z≈0) 0. Biçim F8 (float G7 sonrası).
- Doğrulama N_ULK: ëz/éx/éy 3042/3042 hücre birebir; ëx/ëy 1978/2028 hücre birebir, kalan 50 hücre 7. anlamlı
  basamakta ±1 (ara yuvarlama sırası). Satır 1042/1092, sayı 6892/6942. Derlemde bu tablo 2026'da yalnız N_ULK'da
  basılı (VKF_01'de yok, CP_03 ve H_DVR/sd projelerinde farklı biçimli tablo) → ikinci projede doğrulanamadı.

## 8. s.29 DEPREM YAPI DEVRİLME KONTROLU

- Kat deprem momenti tablosu: §5 ile aynı.
- Kat düşey yük momenti: Wk = Wg + n·Wq (`.FeaSeismic` son K satır, n = ST4 4.9); Xg/Yg = `.EAR` merkez satırının
  1./3. değeri (kütle merkezi). Temel satırı: `.RAD` plakları (2. satır plak sayısı, her plak "no,…,t,…" + köşe
  koordinatları x,y,z) alan×t×γ (γ = /Materials/ 4. alan) ve ağırlık merkezi (shoelace). Moment noktası
  X0/Y0 = plak köşelerinin en küçük x/y'si (−0.4/−0.4). Oran = ΣM/ΣF·H; biçim ΣM float G7, ΣF·H F3, oran F3.
- Doğrulama N_ULK 20/20 satır, 50/50 sayı. Derlemde başka 2026 örneği yok (yalnız 13.1 OTOPARK-*). Radyesiz
  projelerde temel ağırlığı/moment noktası kuralı (sürekli/tekil temel) çözülmedi.

## Kodlar
`scratchpad\ara_genel\tool\`: Yuk.cs (s.3–4), Opsiyon.cs (s.5–6), Sayfa7.cs, Kombinasyon.cs, Aks.cs (s.7–9),
Devrilme.cs (s.11 kat momenti, s.29), ModGraf.cs (s.14), Nokta.cs (s.15–28; `noktaan` analiz komutu),
Purity.cs/OpsMap.cs/Kesif.cs/Feat*.cs (derlem eşleme araçları), Fs.cs/Dump.cs (ikili arama).
