# Bölüm 4 — KİRİŞ SEHİM ve ÇATLAK KONTROLU (TS500-13.2.4)

Araç: sehim\tool (mod `skars <pas> <wk%> 0 1 <c>`; önerilen `skars 5.5 97 0 1 0.00025`), kod Uret.cs, Rijit.cs, Sehim.cs, Bolumler2.cs.

## Satır kuralı
- ST4 /Beams Data/ sırası; aynı adlı ardışık parçalar (KZ-21 iki parça) tek satır.
- N_ULK_02: 64/64 satır. K_BLG_03: GPR 426, üretilen 462 (36 kiriş GPR'de yok — bilinen bodrum/basılmayan kirişler). CP_03: 624/624 (+18).

## Sütunlar
| Sütun | Kaynak / kural | N_ULK | K_BLG_03 | CP_03 |
|---|---|---|---|---|
| uzun δi+δig·λ | BEA +760 ×1000, mutlak değer, F3 | 64/64 | 426/426 | 623/624 |
| Elastik = Ani δi | δt/(1+λ·r); λ=2/(1+50ρ'), ρ'=montaj (KMX +108 adet/+110 ø) / (b·(h−pas)), pas = OPS 10. satır 1. alan (5.5 cm); r = ΣG/Σ(G+Q) LOA kiriş satırları (uzunlukla integral, öz ağırlık hariç). Konsolda ρ' alt düz (+112/+114) | 62/64 | 382/426 | 470/624 |
| Wk | TasarimHesaplari.KirisWk × 0.97, F2 (betonarme Wk'sından ~%3 küçük) | 64/64 | 386/426 | 616/624 |
| fmax | "L/360=" / "L/240=" (OPS 10. satır 2./3. alan) + F2(Ln/sınır·1000, 8 hane) + "w"; w: ST4 kiriş 18. alan (duvar kodu) %10000 ≥ 200 | 63/64 | 318/426 | 272/624 |
| sol/sağ T, M | ankastre-ankastre kiriş, yalnız LOA yükleri (G+Q, öz ağırlık/ST4 G hariç), L = I/J düğüm izdüşümü | T 47,46 / M 46,43 | 257,273 / 219,192 | 191,202 / 201,251 |
| M ac | aynı çözümde açıklık maksimumu | 36/64 | 198/426 | 150/624 |
| durum | δt ≤ fmax → ò (hepsi ò) | 64/64 | 426/426 | 624/624 |
Toplam: N_ULK 597/704, K_BLG 3459/4686, CP_03 4070/6864.

Ln: uçta ST4 2. satır rijit uç (7./8. alan) ≠ 0 ise aks koordinatı ± rijit uç, 0 ise BEA yüz koordinatı (+684/+688 başlangıç, +700/+704 bitiş); BEA +0 veya +8 = 0 (kiriş-kiriş düğümü) ise Ln = düğüm L.
T/M sistematik farkı: GPR'de sol uç momenti küçük, sağ büyük (ΔM ≈ 0.00025·W·L ters simetrik); kaynağı bulunamadı, ampirik düzeltme ile verildi.
Çözülemeyen: kiriş üzerine oturan kiriş tekil yükü (KZ-21/28, CP_03'te çok), konsol fmax (L/240=5.20), mafsallı uç seçimi (N_ULK KZ-13 mafsal, K_BLG KZ-061 ankastre).
