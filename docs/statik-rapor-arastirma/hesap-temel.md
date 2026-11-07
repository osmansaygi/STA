# Bölüm 8–9: temel yükleri, sünek momentler, zımbalama, perde-diyafram

Araç: `temel\tool` (modlar: `temelyuk p1 p2`, `sunek p1 p2`, `mptara "st4|p1|p2" ...`, `diyafram p1 p2`; `FARK=1` farkları döker).

## 8a. TEMELLERE GELEN KOLON YÜKLERİ (N_ULK s.103–105)
- Kaynak: COL (900). N = +26, Alt Mx = +202, Alt My = +378 (22 float dizi).
- Satırlar: blok başı ad + 7 düzen + Zemin. N satırı: G(0), Q_az, "(azaltma Nq= Q(1)×f = Q_az)", Zemin(7). Mx/My satırları 0..7.
  Deprem satırları: indis 8..13 + indis 14; ısı yükü varsa (IsiVar) başlıkta "ISI" ve indis 14 bir kez daha (8 hücre), yoksa 7 hücre.
- Hangi kolonlar: aynı (Yerel, Poligon) grubunun en alt katı VE COL +2 alt düğümü hiçbir kolonun üst düğümü (COL +0) ya da kirişin I/J'si (BEA +32/+34) değil (CP_03 S9-33 kiriş üstünde, basılmıyor). Sıra: kat, yerel no.
- Sayı biçimi: F2 (DosyaTablolari.S), azaltma F3.
- Doğrulama: N_ULK_02 1032/1032 hücre, 144/144 satır (24/24 kolon). K_BLG_03 2643/2806, FHM_GO_A 2079/2193, CP_03 1286/1376, VKF_01 883/946: farkların tamamı azaltma hücreleri (kolon başına 3: Q_az, f, Q_az) + K_BLG'de 1 yuvarlama (437.865).
- Üretilemeyen: hareketli yük azaltma katsayısı f (TS498 kat eksiltmesi, oran .USE 2. satır: "0,0,0,20,40,..."). ≤3 katlı yapıda f = 1.000 (N_ULK birebir). Daha çok katta f kolon başına farklı (0.899–0.951); kat başına Q payı dosyada yok, Q desenlerinden doğrusal elde edilemedi (en küçük kareler hatası 3 t). Yaklaşık: f=1 (fark %5–10).
- Panel uçları (FHM "PB-71-Sol/Sag", K_BLG/CP/VKF'deki fazla adlar): bodrum perde paneli temel yükleri; değerler hiçbir dosyada doğrudan yok (çözülmedi). N_ULK'ta panel yok.
- N_ULK'ta bölüm GPR'de iki kez var (s.1419 tekrar baskı, deprem satırında 7. sütun yok).

## 8b. TEMELLERE KOLONLARDAN GELEN SÜNEK TASARIM DEPREM MOMENTLERİ (s.106)
- Kaynak: COL Alt Mx (+202) indis 8/9 → Mex1/Mex2, Alt My (+378) indis 10/11 → Mey1/Mey2, çarpan f.
- Bodrumsuz (ST4 10. satır 6. alan Hbodrum = 0): başlık "Nez=0.6×Dust×Ne, (Dust=D) TBDY2018-4.10.3.1 Bodrumsuz yapı"; kolon f = 0.6·D ("Kolon  Me=0.6×Dust×Me"), perde f = D ("Perde  Me=Dust×Me ≤ Mp"). D = ST4 14. satır 1. alan (Dfazla).
  Bodrumlu: "Nez=Dalt×Ne, (Dalt=1.5) TBDY2018-4.10.3.2"; kolon ve perde f = 1.5 ("Me=Dalt×Me").
- Perde ölçütü DosyaTablolari.Perde (uzun ≥ 6×kısa). Kolon kümesi 8a ile aynı.
- Doğrulama Me + koşul: N_ULK 96/96 hücre, 24/24 koşul; K_BLG_03 244/244, 61/61; FHM_GO_A 204/204, 51/51; CP_03 128/128, 31/32 (SB-16 GPR'de perde, ölçüt kolon diyor); VKF_01 88/88, 22/22.
- Mpx/Mpy (yalnız perde): dosyada yok, hesap. Dikdörtgen gerilme bloğu (k1=0.85, 0.85·fcd, εcu=0.003, Es=2·10⁶), fcd = SMX +72, fyd = 4200/1.15; N = ilgili yön (G+Q+E) eksenel kuvveti (SMX +40 X, +100 Y); donatı SMX +161 int16 (a5 uç adedi/a6 ø, a7 gövde adedi/yüz, a8 ø), paspayı (çubuk merkezi) 4 cm, uç çubukları iki yüzde a5/2'şer, aralık 17.5 cm, gövde uç bölgeleri arasında eşit.
  Sonuç (5 proje, 70 perde, 140 hücre): zayıf eksen (Bx doğrultusu) çoğunlukla ±0.02 içinde (N_ULK SZ-02 17.64/17.63, SZ-12 17.14/17.15); güçlü eksen ortalama %0.6, en kötü ≈%4 (CP_03 SB-20 933/972). Tam eşleşme 13/140 — uç donatısı yerleşimi STA4 kuralı bilinmiyor; yaklaşık.

## 9a. TEMEL ZIMBALAMA HESAP SONUÇLARI (TS500-8.3) (s.107)
- N_ULK'ta yalnız başlık, satır yok (radye temel; tekil temel yok — ST4 /Single footings/ boş). Diğer 4 projede bu bölüm hiç basılmamış (radyeli olanlarda "RADYE TEMEL KOLON ZIMBALAMA HESABI" ayrı bölüm, bu görev dışı).
- Kural: /Single footings/ boşsa boş tablo/bölüm atlanır. Dolu örnek olmadığı için zımbalama formülü kalibre edilemedi.

## 9b. PERDE-DİYAFRAM KESME YÜKÜ KONTROLU (s.1417–1418, TBDY 7.11.6)
- Perdeler: tüm perde parçaları (S1-02, SZ-02, S1-12, SZ-12).
- Vx = D·(Vxalt − Vxust), Vy = D·(Vyalt − Vyust): Vxalt = COL Tx (+466) indis 8 (Deprem+X), Vyalt = COL Ty (+554) indis 10 (Deprem+Y) bu parçanın; Vxust/Vyust aynı x,y'deki (COL +18/+22) üst kat parçasının değeri, yoksa 0. Sonuç mutlak değer. Metin VB biçimi ("2,5×(1.04-(1.38))=.85": 2 basamak, sondaki sıfır ve baştaki 0 atılır). Doğrulama 24/24.
- Tablo satırı: perde dikdörtgeni 0.01 m dışa büyütülmüş çevresi; kenarlar sol(ß=270, aşağıdan yukarı) → üst(0) → sağ(90, yukarıdan aşağı) → alt(180); kenara dokunan kiriş (bant: aks + hizalama + genişlik) ve döşemeler (kiriş bantları dışında kalan kısım) parçalar; Rx/Ry = parçanın kenar üzerindeki başlangıç/bitiş konumu, L = Ry−Rx, X1..Y2 uç koordinatları; d = kiriş D veya döşeme kalınlığı.
- Asb: kiriş → perdeye bağlanan uçtaki (BEA +32/+34 = perde COL +0 üst düğümü; KMX ham sol/sağ, Y kirişte GPR görüntüsü ters) üst donatı fazlası max(0, mon(+108/+110) + uç üst ilave(sol +128/+130, sağ +136/+138) − As_gerekli(sol +60, sağ +68)); döşeme → DMX düz donatısı cm²/m, kenara dik doğrultu (düşey kenarda X +96/+98, yatayda Y +114/+116). Doğrulama 22–23/25 (kirişlerde 3 hücre +0.01).
- Vxr = fctd·d·L·|cosß| + fyd·Asb′·|sinß| + fyd·Asa′·|cosß|, Vyr = fctd·d·L·|sinß| + fyd·Asb′·|cosß| + fyd·Asa′·|sinß|; kirişte Asb′ = Asb, Asa′ = 0; döşemede Asb′ = Asa′ = Asb·L. fctd = 11.66 kg/cm² (0.35√fck/1.5, iki basamağa kesik), fyd = 3652 kg/cm². Vr = Σ, ✓ ise Vr ≥ V. Doğrulama (GPR geometri hücreleriyle): Vxr/Vyr 48/50, Vr 7/8; kalan ±0.05 farkları GPR'de yuvarlanmış L'den (kesin geometriyle kapanması beklenir).
- Üretilmedi (geometri kodu yazılmadı): çevre parçalama ve Rx/Ry/X/Y/L sütunları; tablo altındaki şekil etiket satırları ("1 2 12 63 43 4 5"). İkinci projede bu bölüm yok.
