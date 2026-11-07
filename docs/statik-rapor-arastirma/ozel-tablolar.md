# Bölüm B — Özel tablolar (N_ULK_02.GPR s.1318–1338) GPR'siz üretim araştırması

Araç: `scratchpad\ara_metraj\B\tool` (AraB.exe; komutlar `sunek`, `temel`, `zemin`, `dengeli`, `yorum`, `kir*`, `tfit*`, `find`, `hex`).
GPR metni `ara_metraj\nulk.txt` (dump), diğer projelerin dökümleri `B\dump\*.txt`.
Sayı biçimi her yerde: float32 → G7 metin → **decimal** ile yarım-yukarı (double ile yuvarlamak 2 hücrede .xx5 sınırında hata verdi).

Sayfa düzeni (N_ULK_02): 1318 kolon süneklilik; 1319–1321 temel sünek tesirleri + toplam temel yükü + TBDY 16.8.3.2 satırları;
1322–1328 zemin gerilmesi / taşıma gücü kontrolü (radye düğümleri); 1329–1331 "ZEMIN GERILMESI" grafik; 1332–1335 kiriş dengeli donatı;
1336 yapı yorumu + kolon/perde alan oranı; 1337 kuşatılmış kolon; 1338 program tanıtım metni (sabit).
N_ULK_02_GEO.GPR (10 s.) yalnız "ZEMİN GERİLMESİNİN, ZEMİN TAŞIMA GÜCÜ…" tablosunu ve grafiği içerir; ana GPR'dekiyle aynı (aynı sonuç: 2592/2601).
260 GPR tarandı: KOLON SÜNEKLİLİK, DENGELİ DONATI ve KUŞATILMIŞ KOLON yalnız N_ULK_02'de var; TEMEL SÜNEK + YAPI YORUMU 12 projede var.

---------------------------------------------------------------------------------------------------
## B1. KOLON SÜNEKLİLİK ALAN KONTROLU — ÇÖZÜLDÜ, 352/352

Kaynak: COL (900 B/kolon) Nz dizisi +26 (22 float), ST4, SMX.
- Satırlar: tüm kolonlar; sıra = yerel no artan, aynı no içinde kat **azalan** (S1-01, SZ-01, S1-02, SZ-02 …).
- Kolon = ST4 adı; Boyut = `{Bx}×{By}` (ST4 /Columns Data/).
- Ng = Nz[0] (G), Nq = Nz[1] (QQQQQQ düzeni; başlıktaki Cqa N_ULK'ta 1 — çok katlı projede azaltma gelebilir, bkz. B2 notu).
- Ne = max(|Nz[8]|,|Nz[9]|,|Nz[10]|,|Nz[11]|) + 0.30·(2/3)·Sds·Ng; Sds = ST4 14. satır 2. alan.
- fck = 1.5·fcd, fcd = SMX +72 (kg/cm²) → t/m² için ×10; Ac = Bx·By/1e4 (poligonda kesit alanı).
- Perde: max(Bx,By) ≥ 6·min(Bx,By) (25×150 perde).
- Sütunlar: `(1.4Ng+1.6Nq)/(Ac·fck)` "< 0.60 ò"; `(Ng+Nq+Ne)/(Ac·fck)` "< 0.40" (perde "< 0.35"); `(Ng+Nq)/(Ac·fck)` "Kolon: x < 0.35 ò" / "Perde: x < 0.25 ò". Ng/Nq/Ne 2 ondalık, oranlar 2 ondalık.
- Doğrulama: N_ULK_02 44 satır × 8 hücre = **352/352**. İkinci proje yok (tablo yalnız N_ULK_02 GPR'sinde).
- Bilinmeyen: sınır aşıldığında basılan simge (tüm satırlar ò).

---------------------------------------------------------------------------------------------------
## B2. TEMELLERE KOLONLARDAN GELEN SÜNEK TASARIM DEPREM TESİRLERİ — kolon tablosu ÇÖZÜLDÜ

Kaynak: COL dizileri (alt uç). Bileşenler: Fx = Tx (+466), Fy = Ty (+554), Fz = Nz (+26), Mx = AltMx (+202), My = AltMy (+378); her dizi [0]=G, [1]=Q, [8]=EX(+%5), [10]=EY(+%5). Birim kN = t × **10** (g=10).
Satırlar: en alt kattaki kolonlar (Kat = min), yerel no sırası. Ezd = 0.3·(2/3)·Sds; g3 = 1.2 (N_ULK_02, başlıkta "3- 1.2 G + Q …") veya 1.0 (eski sürüm GPR'leri "3- G + Q …").
- 1: G+Q; 2: 1.4G+1.6Q (tüm bileşenler).
- Kesme (Fx,Fy): E = e1 + 0.3·e2 (işaretli; e1 = asıl yön [8] ya da [10], e2 = dik yön);
  3: g3·G + Q + Ezd·G + E; 4: 0.9·(G + Ezd·G) + E.
- Eksenel Fz: 3: g3·G + Q + Ezd·G + |e1| (dik yön yok); 4: 0.9·(G − Ezd·G) − |e1|.
- Moment (Mx,My): E = |e1| + 0.3·|e2|; 3: G + Q + Ezd·|G| + E (G katsayısı 1.0, N_ULK'ta da); 4: 0.9·(G + Ezd·|G|) + E.
  Başlıktaki "Me'=0.6×Dust×Me … ≤ Mp" sayılara yansımıyor (çarpan 1; Dust=2.0 projede de aynı).
- Sol sütun: ad; `Bx×By`; `Xg={x,7:F2}`; `Yg=…`. Xg/Yg = ST4 /Column axis data/ (satır = yerel no: x aks no, y aks no, x kodu, y kodu) + /Axis data/ (ilk nx satır x aksları 1001…, sonra y aksları 2001…; nx = ST4 5. satır 1. alan).
  Merkez: kod 0 → aks; −1 → aks + b/2; +1 → aks − b/2; diğer negatif kod → aks + kod/1000 + b/2 (mm kaçıklık; −25 doğrulandı).
- Doğrulama: N_ULK_02 24 kolon × 33 hücre = **792/792**; A_02 **330/330**, F_TIPI **594/594**, 112_ACIL **396/396** (g3=1.0).
  Bodrumlu projelerde (CP_02 873/1056, SB_EMN 1590/1914, KRK_02 803/825, 5176_14 527/693) yalnız 3. kombinasyon farklı; 1, 2, 4 tutuyor.
  CP_02/SB_EMN'de sapma kolona göre değişen Q azaltması gibi (CP_02 SB-01 için Q çarpanı ≈0.68): GPR s.8 "ZEMİN GERİLMESİ HAREKETLİ YÜK AZALTMA DEĞERLERİ" (kat başına eksiltme %) ile Cqa·Q uygulanıyor olmalı — kalibre edilmedi.

Başlık satırları (elle doğrulandı, N_ULK_02): kat adedi, yükseklik, DD, zemin sınıfı, qt (ST4 7. satır 3. alan=30), k (7. satır 2. alan=1680), koordinat, Ss/S1, Sds/Sd1, R, D, DTS, BYS/Hn, BKS/I → mevcut DepremRaporu.Dosyadan kaynakları.
- RADYE TEMEL TABAN ALANI = ST4 /Slab foundations/ plaklarının aks dikdörtgenleri toplamı (5.2·14.85+11.4·16.9+5.2·14.85 = 347.1); TEMEL BOYUTLARI X/Y = plakların aks kapsamı (21.8/16.9).
- TEMEL KOTLARI ALT/ÜST = OPS satırı `1,-1.2,.003,.85,31,.005,-.8,2.5` 2. ve 7. alan (−1.2/−0.8).
- TOPLAM İNŞAAT ALANI = Σ .MASS 1. alan (331.105+358.12 = 689.225).
- DÜŞEY DEPREM YÜKÜ = 10·0.2·Sds·ΣWg, Wg = deprem raporu kat ağırlığı G kısmı (244.35+378.55) → 601.6 (deprem bölümüne bağlı; Wg dosyada bulunamadı).
- Çözülmeyen: TOPLAM TEMEL TABAN ALANI (663.88; 112_ACIL'de sürekli + oturum alanı toplamı tutuyor, N_ULK/A_02'de tutmuyor), TOPLAM BİNA OTURUM ALANI kuralı (radye varsa radye alanı?).

TOPLAM TEMEL YÜKÜ (kN,m):
- Fx, Fy: 3X/3Y/4X/4Y = kolon değerlerinin ham toplamı **8/8**; 1 ve 2 satırında 0.00 basılır.
- Fz: Σkolon + (Gf+Qf), Gf+Qf **çarpansız** her satıra eklenir (6/6, ±0.01). Gf+Qf = 10624.05 kN ("TEMEL ¡ Gf + Qf" satırı) kaynağı **çözülmedi** (radye betonu 347.1 t + 0.8 m dolgu·1.8 + 0.5 t/m² Q ≈ 1020 t; 42 t eksik, bağ kirişleri olabilir).
- Mx/My: X depremi Mx = 10·Σ(F_deprem,i·H_i) (s.29 DEVRİLME tablosu 371.06 tm), My = 0.3·(Y depreminin değeri); Y için tersi; 4 = 3 ile aynı. Kat kuvvetleri deprem raporundan (dosyada float olarak bulunamadı).
- TBDY 16.8.3.2 satırları: ß = atan(√(Fx²+Fy²)/Fz) toplamın 3Y satırından (1.9232°), ic = (1−ß/90)² = 0.9577; Nc = 5.14 sabit; q = Df·γ = 1.8 (γ=1.8 ST4 12. satır 3. alan). Hangi kombinasyonun seçildiği (3Y; 4y daha büyük açı verirdi) tek projeden belirsiz.

---------------------------------------------------------------------------------------------------
## B2b. ZEMİN GERİLMESİNİN, ZEMİN TAŞIMA GÜCÜ TASARIM GERİLMESİNE GÖRE KONTROLU (s.1322–1328) — kısmen

Kaynak: .RAD (yaylı düğümler) + .DE1 (mevcut RadyeVerisi.W): σ = Ko·w.
- G = σ(vaka 0); Q = max σ(vaka 1..6); W = max |σ(vaka 12,13)|; 1.4G+1.6Q ve G+Q+W "x < qt ò" (qt = 30.000, 3 ondalık).
- Doğrulama (E ve G+Q+E hariç): **2592/2601** (9 fark: 3 hücre ±0.001 yuvarlama, 6 özet hücresi E'ye bağlı); _GEO.GPR'de aynı.
- **E sütunu çözülmedi**: DE1 vaka 8–11 ile tutmuyor (düğüm 1: GPR 0.078, vakalar −0.082/0.053/−0.072/0.115; düğüm 9: GPR 0.048, vakalar ≤0.011). Gz min/max/ort satırlarındaki E ve G+Q+E de bu yüzden üretilemiyor. Ez (vaka 16) ve DE1 kayıttaki diğer float'lar da tutmadı. Deprem için ayrı (muhtemelen modal) zemin gerilmesi çözümü gerekiyor.

---------------------------------------------------------------------------------------------------
## B3. KİRİŞLERDE DENGELİ DONATI ORANI ve SÜNEK TASARIM KONTROLU — ÇÖZÜLDÜ, 1761/1792

Kaynak: KMX (300 B) + ST4 kiriş listesi + OPS.
- Giriş birleştirme: aynı kattaki ardışık BEA/KMX kayıtları aynı adı taşıyorsa (KZ-28 = 2 parça, ara düğüm 0) tek giriş; sol uç ilk parçadan, sağ uç son parçadan.
- **Sıralama (sürekli kiriş zincirleri)**: kat kat, kayıt sırasında; öncülü olmayan giriş (aynı Aks'ta J'si bu girişin I'sına eşit giriş yok, I≠0) zincir başı; ardından aynı Aks'ta I = önceki J (J≠0) olan girişler. 64/64 ad ve sıra aynı.
- Satırlar sol / ac. / sag. d = H − 5.5 (OPS kiriş boyuna paspayı 5.5).
  - sol: As = +60, As' = +64; sag: As = +68 (son parça), As' = +72; ac.: As = +76, As' = montaj alanı n·3.1422·ø²/400 (int16 +108/+110; π=3.1422 hücreyle kalibre).
  - ρ = As/(bw·d), ρ' = As'/(bw·d) float32, 7 ondalık; ρb = 0.85·k1·fcd/fyd·600/(600+fyd[MPa]) (fcd = KMX +40, fyd = 4200/1.15) = 0.0204956; ρmax = 0.6·ρb (başlıktaki opsiyon) = 0.0122974; başlık 0.85ρb = 0.017421.
  - "ρ−ρ'" = |ρ−ρ'| "<ρmax ò".
  - As'/As: sol/sag = As'/As "x>0.30 ò"; ac. = max(Asüst,sol, Asüst,sağ, montaj alanı) / (düz n·3.14·ø²/400) "x>0.25 ò".
  - Kirişin ucu 0 düğümünde (kirişe oturan uç) → o satır tüm hücreler "-" (KZ-13 sag, KZ-14 sol). Birleştirilmiş girişte sag As'/As "-".
- Doğrulama: 64 giriş × 28 hücre = **1761/1792**. Kalan 31: 30 hücre ρ−ρ' 7. ondalıkta ±1 (30 cm genişlikli açıklıklar, float sırası), 1 hücre K1-05 ac. oranı (1.27↔1.26).
  π'nin yerden yere farklı çıkması (montaj 3.1422, düz 3.14) kalibrasyon bulgusudur; ikinci proje yok.

---------------------------------------------------------------------------------------------------
## B4. İMAR YÖNETMELİĞİ, KAPALI ÇIKMALI YAPIDA KUŞATILMIŞ KOLON KONTROLU — sabit metin + boş tablo

N_ULK_02'de tablo satırsız: üstte sabit 3 satır ("11.3.2025 tarihli Planlı Alanlar Yönetmeliği… gerek yoktur. A12, A13 ve A32 … zorunludur."), başlık "Kolon / Kiriş konumu / Kiriş / bw / bc / bw/bc / AÇIKLAMA" ve boş gövde.
Satır üreten örnek hiçbir GPR'de yok (260 GPR); satır kuralı kalibre edilemedi. Mevcut yönetmelik notuna göre her zaman boş tablo + not basmak GPR ile aynı (1/1 sayfa).

---------------------------------------------------------------------------------------------------
## B5. STA4-CAD YAPI YORUMU — ÇÖZÜLDÜ (dosyadan birebir)

Kaynak: `<proje>.YORUM` (Windows-1254 metin; STA4 analizle birlikte yazar). GPR "STA4-CAD YAPI YORUMU" başlığından sonraki metin, boş satırlar dışında satır satır aynı; "KOLON VE PERDE ALAN ORANI" tablosu (ApX/ApY sütunlu) da dosyada.
- Doğrulama (boş olmayan satır): N_ULK_02 **37/37**, CP_02 50/50, 5176_14 39/39, SB_EMN 40/40, KRK_02 39/39, A_02 37/37, F_TIPI 38/38, 112_ACIL 37/37.
- Dikkat: YORUM analiz anında yazılır; GPR'den sonra yeniden analiz yapılırsa ikisi farklılaşır (tarih denetimi yeterli).
- Sayıların kaynağı (bilgi): Tx/Ty FeaMod; toplam yapı alanı Σ .MASS 1. alan; toplam yapı yükü Σ(Wg+Wq) deprem kat ağırlıkları (819.2); θ/β ikinci mertebe ve αm deprem raporu; temel alanı radye alanı. Alan oranı tablosundaki Ak = .MASS 1. alan.

---------------------------------------------------------------------------------------------------
## Özet

| Tablo | Kaynak | Doğrulama | Durum |
|---|---|---|---|
| Kolon süneklilik alan kontrolü | COL Nz[0,1,8–11], ST4 Sds, SMX fcd | 352/352 | çözüldü |
| Temel sünek tesirleri (kolon satırları) | COL alt uç dizileri, ST4 aks/kolon aks verisi | 792/792 (+A_02, F_TIPI, 112_ACIL %100) | çözüldü; bodrumlu projelerde kombinasyon 3'te Q azaltması eksik |
| Toplam temel yükü | kolon toplamları + Gf+Qf + 10·ΣF·H | Fx/Fy 8/8, Fz 6/6 (Gf+Qf hariç) | Gf+Qf kaynağı yok; momentler deprem raporuna bağlı |
| Zemin gerilmesi taşıma gücü kontrolü | RAD+DE1 | 2592/2601 (E hariç) | E sütunu çözülmedi |
| Kiriş dengeli donatı / sünek tasarım | KMX, BEA/ST4 zincir sırası, OPS paspayı | 1761/1792 | çözüldü (31 hücre ±1 son hane) |
| Kuşatılmış kolon | sabit metin, boş tablo | 1/1 | satır kuralı bilinmiyor |
| Yapı yorumu (+alan oranı tablosu) | .YORUM | 8 projede %100 | çözüldü |
