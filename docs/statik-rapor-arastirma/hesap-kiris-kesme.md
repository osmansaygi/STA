# Bölüm 7 — Kiriş kesme/burulma ve askı donatısı (GPR'siz üretim)

Araç: `kiriskesme\tool` (Ara.exe; `kar [x]` kesme/burulma karşılaştırma, `askikar` askı karşılaştırma,
`fit2/aotest/brute/brutev/x0/lnimpl` kalibrasyon modları). Kod: Uret.cs (tablo), Parca.cs (uç/x0), Aski3.cs (askı).

## A) KİRİŞLERİN KESME ve BURULMA KONTROLU (TS500-8.2) — N_ULK s.101

Kaynak: BEA (900 B) + KMX (300 B) + ST4 /Materials/ (fck) + kiriş B/H (ST4).
- BEA 22-float dizileri: +214 sol uç burulma T, +302 sağ uç T, +390 sol V, +478 sağ V (indis 0 G, 1..6 Q düzenleri,
  8..11 deprem ±X/±Y (8/9 ve 10/11 birbirinin tersi DEĞİL: dışmerkezlik durumları), 14 ısı).
- BEA +16/+20 (sol), +24/+28 (sağ): uç rijit/mesnet bileşenleri p1,p2 (s = p1+p2 = mesnet genişliği);
  +684/+688 → +700/+704: kiriş yüz noktaları; Ln = aralarındaki uzaklık.
- Aynı adlı ardışık ST4 parçaları (KZ-21, KZ-28 gibi) TEK kiriş: sol uç ilk parçadan, sağ uç son parçadan,
  Ln = ilk parçanın sol yüz noktası ile son parçanın sağ yüz noktası arası.
- Kombinasyonlar: 1.4G+1.6Q_i (i=1..6) ve G+Q1±E_e (e=8..11, ± ikisi de; desen Q yok). Rijit uçta (x0=0) depremli
  kombinasyonlar alınmaz (K_BLG perde uçları).
- Kesit kesmesi: V(x0) = |V_uç| − |q|·x0, q = (V_sol − V_sağ)/Ln yalnız düşey kısımdan (G,Q katsayıları; E hariç).
  x0 = a + min(d, Ln/3), a = s/2; p1 veya p2 tam 0 ise x0 = a (d eklenmez); a > Ln/3 ise x0 = 0.
- Seçim: her uçta Tr = (V/Vcr)² + (T/Tcr)² en büyük kombinasyon; tablodaki Vd, Td, Tr = Tr'si büyük uç;
  Aov/s, Aot/s, Ao/s = iki uçtan (Aov+Aot)'u büyük olan uç (62/62 doğrulandı). Td = o ucun T'si (kesite indirgenmez).
- d = H − 5.5 cm. fctd = 0.35·√(fck/10)/1.5·10·k (kg/cm²), k = 0.99927 (C25), 0.99932 (C30) — başlıkta 2 haneli basılır
  (11.66 / 12.77). Vcr = 0.65·fctd·bw·d; Vc = 0.8·Vcr.
- Etriye: KMX +146 ø, s = +150 (>0) yoksa +148, kol = 4 (KMX +144 ≥ 2) yoksa 2; alan π=3.142 ile.
  Asw/s = n·π·ø²/4/s; Vw = Asw/s·3652.35·d (Vw için fywd 3652.35; Aov/Aot için 4200/1.15).
- Tcr = 1.35·fctd·Sx, Sx = Σ x²y/3 (x kısa kenar): gövde bw×H + iki tabla dikdörtgeni hf×f
  (hf = KMX +2/+6, f = 0.06·Ln·100 cm; KMX +0/+4 tamsayısı ⌊0.06Ln⌋'den küçükse o değer — döşeme kenar sınırı).
- Aov/s = max(0, Vd−Vc)/(2·d·fywd); Aot/s = Td/(2·Ae·fywd), Ae = (bw−11)(H−11); Ao/s = toplam; ò: Ao/s ≤ Asw/s ve Vd ≤ Vr.

Doğrulama (hücre, ad üzerinden eşleşme; 12 sütun):
- N_ULK_02: 700/768 (64 satır). Vcr, Vw, Vr, Vd, Td, Aov, Aot, Asw, ò 64/64; Ao 63/64; Tcr 26/64; Tr 35/64.
- K_BLG_03 (GPR BEA'dan yeni): 4248/5112 (426 satır). Vcr/Vw/Vr/Asw/ò 426/426, Td 423, Vd 346, Aov 395, Aot 391, Tcr 12, Tr 184.
- FHM_GO_A 4811/5616, CP_03 5926/7488, VKF_01 2728/3336 (bu üçünde GPR BEA'dan eski; Vd/Td farkının bir kısmı bundan olabilir).
Kalan sapmalar:
- Tcr: 4. ondalıkta ±1–7 (relatif ~1e-4). Etkin Ln/tabla genişliği STA'da çok az farklı (KZ-01/KZ-02 simetrik ikizde bile
  0.0002 fark); KZ-13/14 (bir ucu kirişe oturan) tabla 0.076·Ln çıkıyor — kural bulunamadı (Tcr 2.3335 / GPR 2.5154);
  K_BLG KZ-060… (iki uçta 0 bileşen) tabla 0.08·Ln. Tr sapmaları Tcr'den geliyor.
- Vd: K_BLG'de ~40 satır ±0.01 (q yaklaşığı), kısa kirişlerde (Ln ≤ 1.4) ve Ln'si yüz noktalarından farklı kirişlerde
  (KB-038: STA Ln≈5.05, bizim 4.95) birkaç ondalık. Satır SIRASI GPR ile aynı değil (STA çok parçalı kirişleri ve bazı
  aksları farklı sıralıyor) — ad ile eşleştirildi.

## B) KİRİŞ ASKI DONATISI KESME KONTROLU (TS500-8.1.2) — N_ULK s.102

Kaynak: BEA +32/+34 (int16 düğüm no, GPR I/J ile aynı), COL +0 (kolon üst düğümü), KMX, ST4.
- Satır: kolon olmayan, ≥3 kiriş ucunun buluştuğu düğüm; içinden geçen aynı adlı parça çifti = ana kiriş, diğer uçlar = tali
  ("A+B" ile birleşir). Tali iki parçalı ve kesiti ana kirişten küçük değilse roller değişir (ana = tali tarafının
  düğümde sağ ucu olan parçası; GPR "KB-33 | KB-09+KB-09"). Aynı adlı geçen çift yoksa satır yok.
- Askı Donatısı "í{ø}/9": ø = ana kiriş KMX +146, aralık sabit 9 cm. Asw = 2·π·ø²/4 (π=3.142). Bw/H = ana kiriş.
  Vc = 0.8·0.65·fctd·bw·d, Vw = Asw/9·3652.35·d, Vr = Vc+Vw (ana kiriş d = H−5.5).
Doğrulama: N_ULK 18/18 hücre (2 satır, Vd dahil); FHM_GO_A 107 satırın 103'ü üretildi, Vd hariç 8 sütun 824/824;
CP_03 142/142 satır, Vd hariç 1136/1136; VKF_01 258/288.
Üretilemeyen: Vd (tali uç kesmesi). N_ULK'de |V_G,uç| − q_G·x0 (yalnız G) 2/2 tutuyor ama FHM/CP_03'te tutmuyor; 1.4G+1.6Q,
G+Q±E, farklı x uzaklıkları denendi — işaretli (negatif de olabiliyor: −0.03) bir değer, kaynağı çözülmedi.
FHM'de 4-yönlü düğümde iki X/Y çifti eşitken 4 satırda rol ters seçildi (KZ-28/KZ-75+KZ-76 ↔ GPR KZ-76/KZ-28+KZ-28).
