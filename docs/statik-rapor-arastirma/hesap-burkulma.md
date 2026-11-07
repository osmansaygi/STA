# Bölüm 5 — KAT BURKULMA HESABI / KOLON BURKULMA HESAP SONUÇLARI (N_ULK_02 s.85–91)

Araç: `burkulma\tool` (`Ara.exe kars 1 1 1 0` karşılaştırma, `Hesap.cs` = üretim kuralı). Burkulma tablosu yalnız N_ULK_02 GPR'sinde var (diğer 4 projede yok) → ikinci proje doğrulaması yapılamadı.

## Kolon blokları (88 yarım blok = 44 kolon × X/Y, 20 hücre)
Kaynak: ST4 (/Columns Data/ Bx/By, 2. satır 7./8. alan üst/alt kot, /Axis data/), COL, BEA, KMX. Sıra: kolon yerel no artan, her kolonda üst kat önce; her blok sol X, sağ Y.

| Alan | Kural | GPR |
|---|---|---|
| Hk | `(float)UstKot − (float)AltKot` (single!). İSTİSNA: ötelenen kat/yönde (é>0.05; N_ULK: kat 1 X) üstünde kolon olan kolon: Hk = üst kolonun üst kotu − alt kot (7.65) | 88/88 |
| Ix / Iy | X: By·Bx³/12, Y: Bx·By³/12 (m⁴); Ac = Bx·By | 88/88 |
| (I/L)c | single: I / Hk; üst/alt komşu kolon kendi Hk'si ile. Ötelenen katta alttaki kolon için gösterilen değer I/(üst kolon üstü − alt kot) | 88/88 |
| düğüm | COL +0 üst düğüm, +2 alt düğüm; kiriş BEA +32/+34 düğümleri; X yönü = 2xxx akslı kirişler (St4Kiris.YYonu=false) | |
| (I/L)b | Σ 0.35·I_T / ln. I_T: T kesit, gövde bw×h, tabla kalınlığı hf = KMX +2/+6, kenar genişlikleri KMX +0/+4 (cm int); kenar = 0.06·ln eğer round(0.06·ln·100) = KMX değeri, değilse KMX/100 (TS500: 0.1·lp, lp = 0.6·ln). ln = |aks(AksSon) − aks(AksBas)| − BEA(+20) − BEA(+24) (aynı adlı bölünmüş kirişte zincirin ilk parçası +20, son parçası +24). Ötelenen katta tuhaf blok: üst = (üst kolon üst düğümü kirişleri) + 0.5·(kendi üst düğüm kirişleri), alt = 0.5·(kendi üst düğüm) | 158/160 (±%0.4 içinde 160/160) |
| ψu, ψa | 0.7·Σ(I/L)c / Σ(I/L)b, en çok 20; temel ucu (altta kolon yok) ψa = 0. Tuhaf blokta ψu = 0.7·I/(Hk−hb)/IbU | 160/160 |
| k | TS500/ACI nomogram denklemi (önlenmiş: GAGB/4·x² + (GA+GB)/2·(1−x/tan x) + 2tan(x/2)/x = 1; önlenmemiş: (GAGB·x²−36)/(6(GA+GB)) = x/tan x; x = π/k). STA4 değeri tam çözümden sistematik 0…0.0007 (önlenmiş), 0.0018'e kadar (önlenmemiş) küçük; ψ≈0.5'te en büyük, ψ→0 ve 20'de sıfır — STA4 yaklaşık bir çözüm/çizelge kullanıyor, bulunamadı | 47/80 tam, 80/80 ±0.002 |
| Lkr | k·(Hk − hb), hb = üst düğümdeki o yön kirişlerinin en büyük yüksekliği | 15/80 tam, 80/80 ±%0.4 (k farkından) |
| Cm | önlenmiş: 0.6+0.4·M1/M2 ≥ 0.4; Md = 1.4G+1.6Q (COL dizilerinde indis 0 ve 1); X yönü Mx (+114 üst/+202 alt), Y yönü My (+290/+378). Eğrilik: m(0)=Malt, m(H)=−Müst (aynı işaret = çift eğrilik), üst uç kiriş altı yüzünde z=Hk−hb alınır; M2 mutlak büyük olan. Ötelenen kat: Cm=1 | 71/80 (9 farklı: S1-01/03 Y yüz kaydırması yok gibi, S1-22/24 Y, S1-23 Y, SZ-17 Y, SZ-12 Y, S1-11 X — çözülemedi) |
| Rm | önlenmiş: 1.4G/Nd; ötelenen kat (ve tuhaf blok): 0 ("Rm=ΣVg/ΣVd" ya da "Ng/Nd" etiketi) | 80/80 |
| Nd | 1.4·Nz[0] + 1.6·Nz[1] (COL +26) | 80/80 |
| EI | 0.4·Ec·I/(1+Rm), Ec = /Materials/ 1. satır 3. alan ×10 (302500 kg/cm² → 3 025 000 t/m²), double | 75/80 (5'inde son hane ±1–2) |
| Nkr, β, λ | π²EI/Lkr²; β = Cm/(1−1.3Nd/Nkr); λ = Lkr/√(I/Ac) | Nkr 0/80 tam (3 ondalık, Lkr'ye duyarlı) ama 80/80 ±%0.4; β 71/80; λ 65/80 tam, 80/80 ±0.1 |
| metin | "yanal deplasman onlenmemis" yalnız ötelenen kat/yönde üstünde kolon olmayan kolonda; ötelenen kat/yönün tüm satırlarında "(Lk/i)>35/√… >>ß=ßx.ßs", diğerlerinde "<…>>ß=ßx" | 88/88 |

Toplam (S1-10, S1-14, SZ-10, SZ-14 hariç 80 yarım blok × 20 alan): tam 1378/1600, ±%0.4 içinde 1504/1600. Tüm 88: tam 1486/1760.
Üretilemeyen: düğüm 40 (S1-10/S1-14 alt, SZ-10/SZ-14 üst) — GPR (I/L)b X = 0.00003786, Y = 0.7 (STA4 tuhaflığı; o düğümde X kirişi yok, Y'de KZ-18/19 var); ψ 11.174/0.002 ve bağlı k, Lkr, Nkr değerleri.

## Kat tablosu (4 satır: kat 1 X/Y, kat 2 X/Y; kat no = ST4 kat indisi, 1 = Zemin)
| Sütun | Kural | GPR |
|---|---|---|
| ΣNg (başlık "äNg") | aslında ΣNd = Σ(1.4G+1.6Q) kattaki kolonlar | 4/4 (1174.6, 456.7) |
| ΣNkr | o kat/yöndeki kolon bloklarının Nkr toplamı | GPR Nkr'leriyle 4/4; bizim Nkr ile ±%0.3 |
| βs | é>0.05 ise 1/(1 − 1.3·ΣNd/ΣNkr), değilse 1 | formül 4/4 (é verilirse) |
| Σ(Δi·Ndi/L), Vfi, é | é = 1.5·Σ/Vfi. Vfi ≠ kat kesme kuvveti (deprem raporu 60.75/31.47), ≈ Σ kolon kesmesi: kat 1 X ΣTx(EX)=37.197 (GPR 37.142), Y 70.789 (69.958); kat 2 X Σ(|Mü|+|Ma|)/(H−0.5)=37.318 (37.372), Y 57.6 (55.906). Δi (kolon göreli ötelemesi) hiçbir dosyada bulunamadı (DIS 84×240 bayt çözülemedi; deprem raporu ötelemeleriyle Σ tutarlı değil) | 0/4 (yaklaşık: Vfi %0.1–3 sapma, Σ yok) |

Not: é sınırı (0.05) kolon bloklarının "ötelenen kat" kuralını belirliyor; N_ULK_02'de yalnız kat 1 X (é=0.0619). GPR'siz üretimde é bilinemediği için ötelenen kat kararı yaklaşık kalır.
