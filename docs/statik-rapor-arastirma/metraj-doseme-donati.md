# DÖŞEME DONATI KEŞİF METRAJI — GPR'siz üretim araştırması (N_ULK_02, s.33–34, 37)

Araç: `ara_metraj\D\tool` (komutlar: `dosdon`, `dosadet`, `doskarsi`, `dosila`, `doskg`, `dosmodel`, `flof`). Dökümler: `D\dump.txt`, `D\adet.txt`.
İkinci proje: metraj tabloları yalnız N_ULK_02.GPR'de var (D:\projeler altındaki 1131 GPR tarandı) → ikinci projede doğrulama yapılamaz.

## Sonuç özeti
| Hücre | Kaynak | Doğrulama | Durum |
|---|---|---|---|
| Satır yapısı + etiket (X/Y düz, pilye, sol/sag ila) | DMX | 207/207 satır, 0 döşemede satır sayısı farkı | çözüldü |
| Çap | DMX | 207/207 | çözüldü |
| Adet | DMX aralığı + ST4 geometri | 145/207 (%70) | kısmen |
| Boy | ST4 geometri (STA4 döşeme çizim algoritması) | basit modelle 31/207 | çözülmedi |
| Ağırlık (özet kg) | Σ adet·boy·πø²/4·7.85 | GPR satırlarından 4/4 kat-çap ±0.2 kg | formül çözüldü (boya bağlı) |

Sonuç: Döşeme donatı metrajı dosyalarda saklanmıyor (DMX/FLO/ST4 dışında boy/adet hiçbir dosyada yok; 974, 752, 1113 gibi boylar int16/float olarak aranıp bulunamadı). STA4 bunu yazdırma anında döşeme çizim (şerit) algoritmasıyla üretiyor; boylar asimetrik (ayna döşemeler farklı: DZ-09 X pilye 762 / DZ-10 974; D1-08/D1-11 farklı) ve /Floors Continuous/ şerit sırası ile FLO'daki birleşik konsol açıklıklarına (D208 FLO L=1.45×10.70, aynası D211 1.30×8.10) bağlı. Birebir üretim STA4 döşeme çizim algoritmasının yeniden yazılmasını gerektirir.

## 1. Satır/etiket/çap kuralı (207/207)
DMX kaydı 250 bayt, ST4 /Floors Data/ sırası. X bloğu +96, Y bloğu +114, int16 dizisi:
`[ø, s, bayrak, pø, ps, solø, sols, sağø, sağs]` (bayrak 1 = pilyeli, 2 = yalnız düz).
Satır sırası (her yön için): `düz` (ø>0, s>0) → `pilye` (bayrak==1 ve pø>0; D1-05/DZ-11'de bayrak 1 ama pø=0 → pilye satırı yok) → `sol ila` (+10/+12) → `sag ila` (+14/+16). Önce X sonra Y. Metraj tablosunda Y için sol/sağ TERS DEĞİL (betonarme tablosundaki ters gösterim burada yok: DMX Y +128/+130 → "Y sag ila").
Döşeme sırası: döşeme no artan (DS, DZ, D1 …), boş satır üçlü hücreye soldan sağa dolar (3 kayıt/satır), döşeme başına yeni satır.
Etiket metinleri: `X düz`, `X pilye`, `X sol ila`, `X sag ila`, `Y …`.

## 2. Adet kuralı (145/207)
n = ⌈L_dağ / s⌉ (float32), s = DMX aralığı (cm), L_dağ = çubuğa dik net açıklık:
- X çubukları Ly boyunca, Y çubukları Lx boyunca dağılır.
- L_dağ = aks açıklığı − kenar kirişinin döşeme içine taşan genişliği (kalıp tablosundaki net Lx/Ly ile aynı; |f5|>d·10 merdiven sahanlığında kırpma yok), AMA rijit ucu hiç olmayan kiriş (ST4 kiriş 2. satır Ri=Rj=0, ör. K1-05/K1-08) düşülmez (D1-09/D1-10 X: 10 adet).
- float gürültüsü belirleyici: DZ-03 X 3.20/0.40 → GPR 9 (bizde 8), DZ-09 X 7.60/0.20 → 38 (ceil ile tutuyor).
Tutmayan gruplar:
- x=10.375–15.8 döşemeleri (DZ-05, DZ-18, D1-04, D1-18): Y adetleri L_dağ=517.5 ister (sağdaki 1004 aksı kirişi düşülmüyor, soldaki 1003 düşülüyor); 1005 aksındaki aynı tip kiriş düşülüyor → kural bulunamadı. (4 döşeme, 8 hücre)
- Konsollar (DZ-01/02/06/08/15/20/21/22, D1-01/02/05/07/08/11/19/20/21/22): ana çubuklarda ⌊L/s⌋+1 ya da yalnız mesnet kirişinin boyu (DZ-01 Y: 27 = ⌊520/20⌋+1, mesnet kirişi 0–5.2), dağıtma çubuklarında ⌊L/s⌋ benzeri; ayna konsollar farklı (D1-08 Y 7 / D1-11 Y 6; D1-07/D1-19 Y 26 = 5.2 m kapsıyor, D1-06/D1-16 33). (~25 hücre)
- Merdiven/sahanlık döşemeleri (DZ-23…32, tip 1/2, f5≠0): adetler komşu sahanlıklarla birleşik şeritten (DZ-23 X 22, Y 22; DZ-29 X 6 = Lx 1.10 üzerinden) — kural yok. (~20 hücre)

## 3. Boy kuralı (çözülmedi) — gözlenen bağıntılar
GENEL BETONARME/DÖŞEME opsiyonları (GPR s.5): plak paspayı 3 cm, max demir 1200, bindirme ø×50, kanca ø×10, Lk (üst donatı uzatma) = 50ø / Ln/4, Lpa = Lnet1/5, min Lp = Lnet1/2.
- Düz: ≈ aks açıklığı + 25…30 (açıklık ≤ ~5.5 m), + 12…15.5 (≈ 8 m açıklık). Aynı geometride ±3 cm sapmalar (DZ-17 548 / D1-13 545; DZ-17 Y 395 / DZ-18 Y 398) → boy, döşemenin kendi dışındaki verilere (şerit, komşu) bağlı.
- Pilye: düz × 1.34…1.82 (komşu döşemeye Ln/4 + büküm); ayna döşemelerde çok farklı (DZ-09 762 / DZ-10 974).
- İç mesnet ilavesi: ≈ max(Ln1, Ln2)/2 + bw, aşağı yuvarlama (DZ-03 sağ 275 = 490/2+30 ✓, DZ-07 sol 288 = 517.5/2+30 ✓, D1-14 sol 283 ✓; DZ-04 sağ 282 beklenen 283 ✗). x=10.375–15.8 döşemelerinin Ln'i burada da 517.5.
- Konsol mesnet ilavesi: ≈ bw + 2·Lkonsol(FLO +116/+120 birleşik açıklık) + 20…30 (D1-09 sol 340 = 30+2·145+20; D1-10 sağ 310 = 30+2·130+20).
- Basit model (düz = aks+28/14, pilye = 1.5·düz, ilave = max(Ln)/2+bw): boy 31/207 birebir; kg sapması zemin ø8 −8.4 %, ø10 −43 % (merdiven), ø12 −2.4 %; normal kat ø8 −4.2 %.

## 4. Ağırlık kuralı (özet tablosu için)
kg = Σ adet × boy(m) × π·ø²/4 × 7.85 (ø mm → kg/m = 0.0061654·ø²). GPR satırlarından: zemin ø8 2232.09 / GPR 2231.9, ø10 596.17 / 596.1, ø12 946.66 / 946.8, normal ø8 2450.50 / 2450.3 (fark ±0.2 kg = basılan boyun cm'ye yuvarlanması; STA4 yuvarlanmamış float boyu kullanıyor). 0.00617·ø² ile fark +1.9 kg → STA4 πø²/4·7.85 kullanıyor. (Not: temel tablosunda bu kural tutmuyor; orası ayrı.)

## 5. Yan bulgular (FLO, 200 bayt/döşeme)
- +60/+64/+68/+72 int16: sol/sağ/alt/üst kenar kirişi = BEA kayıt no + 1 (D209: 55→216 sol, 58→219 sağ, 40→203 alt, 65→205 üst).
- +76/+78/+80/+82 int16: aynı kenarlardaki komşu döşeme kimliği (tam eşleme doğrulanmadı).
- +84…+112 float (8 adet): kenar kirişi genişlikleri, kenar başına (dış, iç) çifti: sol (+84 dış, +88 iç), sağ (+92 iç, +96 dış), alt (+100 dış, +104 iç), üst (+108 iç, +112 dış). D103: +88 .30, +96 .30, +104 .25, +112 .25. Bizim KenarTasma ile tutarlı.
- +116/+120 float: hesap açıklıkları; konsollarda komşu konsolla birleşik (D208 1.45×10.70, D205 1.45×4.30), ayna döşemede birleşmeyebilir (D211 1.30×8.10) → asimetrinin kaynağı.

## Üretilemeyen değerler ve neden
Boylar (ve konsol/merdiven adetleri) hiçbir dosyada yok; STA4'ün yazdırma anındaki döşeme şerit/çizim algoritmasının çıktısı. Yaklaşık hesap mümkün (yukarıdaki model; kat toplam kg sapması zemin −12 %, normal −4 %). Kesin üretim için öneri: STA4 kalıp/donatı çizim çıktısı (DWG) ya da KSF dosyası (yalnız kat toplamları; GPR ile birkaç kg farklı olabilir) kullanmak.
