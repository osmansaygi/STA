# Kiriş donatı çizimleri — referans

Kaynak: **Ahmet TOPÇU, Betonarme I, Eskişehir Osmangazi Üniversitesi, 2019** (`C:\Users\BEYKENT\Desktop\Kiris_1.pdf`, sf. **154–179**). Kiriş sınır değerleri, kesit/donatı hesabı ve **kiriş açılımı (şantiyeye giden donatı çizimi)** kuralları.

Bu dosya KIRISACILIMLARI / KOLONDUSEY / KALIP50 gibi donatı çizimi işlerinde ortak sözlük ve kural kaynağıdır.

---

## 1. Semboller (notasyon)

| Simge | Anlam |
|---|---|
| `bw` | kiriş gövde genişliği |
| `h` | kiriş yüksekliği |
| `d` | faydalı yükseklik = `h − d'` |
| `t` | tabla kalınlığı |
| `cc` | **net beton örtüsü** (donatı ile beton yüzü arası) |
| `d'` | beton örtüsü (donatı merkezinin dış yüze uzaklığı) |
| `c'c` | net donatı aralığı (komşu çubuklar arası) |
| `bk` | kolonun kirişe **dik** kenarı |
| `hk` | kolonun kirişe **paralel** kenarı |
| `Ln` | kiriş **net açıklığı** (kolon/perde yüzleri arası) |
| `Lc` | sarılma bölgesi uzunluğu |
| `c` | donatının **komşu açıklığa uzatılma** miktarı |
| `a` | ilk/son kolonda donatının **kolon içindeki** uzunluğu |
| `b` | komşu açıklıkta devam ettirilemeyen boyuna donatının **90° bükülen** kısmının uzunluğu |
| `φ` | boyuna donatı çapı |
| `ρ` | çekme donatısı oranı |
| `ρ'` | basınç / montaj donatısı oranı |
| `ρ1` | mesnet **üstündeki** donatı oranı |
| `ρ'1` | mesnet **altındaki** donatı oranı |
| `φw` | etriye çapı |
| `ρw` | etriye donatısı oranı |
| `s` | açıklıkta etriye adımı |
| `s'` | sarılma bölgesinde etriye adımı |
| `e` | kesitteki etriyelerin komşu iki düşey kolu arası mesafe |
| `k` | etriye kanca boyu |
| `φgövde`, `ρgövde` | gövde donatısı çapı / oranı |

---

## 2. Kirişlerde sınır değerler

`TS500:2000 | TBDY-2018 | Ek öneri` — çakışmazsa **TBDY-2018** uygulanır (TBDY 7.2.2).

| Tanım | TS500:2000 | TBDY-2018 | Ek öneri | Not |
|---|---|---|---|---|
| min `bw` | 20 cm | 25 cm | 25 cm | bkz. Aç.1 |
| max `bw` | `bk+h` | `bk+h` | – | dar kolona geniş kiriş oturtma (7.4.1.1a) |
| min `h` | 30 cm, `3t`, `Ln/10` | 30 cm, `3t` | 40 cm | sehim |
| max `h` | `Ln/2.5` (sürekli), `Ln/1.5` (basit) | `3.5bw`, `Ln/4` | – | aşarsa **yüksek kiriş** |
| min `ρ` | `0.8 fctd/fyd` | – | – | |
| max `ρ` | 0.02 | 0.02 | – | |
| min `ρ1` | `0.8 fctd/fyd` | `0.8 fctd/fyd` | – | |
| max `ρ1` | 0.02 | 0.02 | – | |
| min `ρ'` | – | `ρ1/4` | – | **montaj donatısı alt sınırı** |
| min `ρ'1` | – | `0.5ρ1`, `0.8 fctd/fyd` | – | **mesnet altı** donatısı alt sınırı |
| max `ρ'1` | – | 0.02 | – | |
| max `(ρ−ρ')` | `0.85ρb` | `0.85ρb` | – | |
| sehim sınırı | – | – | `ρl = 0.235 fcd/fyd` | aşarsa sehim hesabı |
| max `(ρ1−ρ'1)` | `0.85ρb` | `0.85ρb` | `ρl` | |
| min `Lc` | `2h` | `2h` | – | sarılma bölgesi |
| max `s` | `0.5h` | `0.5h` | 20 cm | |
| min `s` | – | – | 10 cm | |
| max `s'` | `h/4`, 15 cm | `h/4`, 15 cm, `8φmin` | `s/2`, 10 cm | φmin: min boyuna çap |
| min `s'` | – | – | 5 cm | |
| max `e` | – | 35 cm | – | bkz. Aç.4 |
| min `φ` | 12 mm | 12 mm | – | |
| max `φ` | – | – | 24 mm | |
| min `φw` | 8 mm | 8 mm | – | |
| max `φw` | – | – | 12 mm | |
| min `ρw` | `0.3 fctd/fywd` | bkz. 7.4.5.2 | – | fywd: etriye çeliği |
| min `φgövde` | 10 mm | 12 mm | 12 mm | |
| min `ρgövde` | 0.001 | `0.3(ρ1+ρ'1)` | – | |

**Açıklamalar**
1. `bw ≤ bk+h`, `bw ≥ 25 cm`, `h ≥ 30 cm` (TBDY 7.4.1.1a).
2. Sehim: `ρl ≤ 0.235 fcd/fyd` ve TS500 13.2 yükseklik/açıklık oranlarına uyulursa sehim hesabı yapılmayabilir.
3. Bu koşulları sağlamayan kiriş **yüksek kiriş** olarak tasarlanır (7.4.1.1b/c).
4. `bw > 40 cm` ise çok kollu (4, 6…) etriye kullanılmalı (7.4.4).
5. **Gövde donatısı:** `h > 60 cm` ise her iki yüze gövde donatısı; `Asgövde ≥ 0.001 bw d`, aralık `a ≤ 30 cm`.

---

## 3. Kenetlenme, uzatma ve kanca (sf. 156)

| Tanım | TBDY-2018 | Ek öneri | Not |
|---|---|---|---|
| min `a` (kolon içi) | `0.4 lb` | – | kenar mesnet |
| min `b` (90° büküm) | `12φ` | – | |
| min `(a+b)` | `lb` | `50φ` | `lb`: kenetlenme boyu |
| min `c` (komşu açıklığa uzatma) | – | **`Ln/4`** | mesnet ek donatısı / komşu açıklıktan gelen |
| min `(hk+c)` | `lb`, `50φ` | – | |
| min `k` (etriye kancası) | `6φw`, 8 cm (nervürlü) | `10φw`, 10 cm | 135° kıvrımlı kanca |
| max `Nd` | `0.1 fck Ac` | – | aşılırsa **kolon** olarak boyutlandır |
| max `Vd` | `0.22 fcd Ac` | – | |
| min net donatı aralığı | `2.5 cm`, `φ` | **5 cm** | |
| min çekme donatısı sayısı | 2 | **3** | |
| min montaj donatısı sayısı | 2 | 2 | |

**Beton örtüsü**
- `cc ≥ 2 cm` içte, `≥ 2.5 cm` dışta (TS500); `c'c ≥ 2.5 cm` ve `≥ φ`; ikisi de `≥ (4/3)·Dençok`.
- Öneri: `cc ≥ 3 cm`; yangın (2–4 saat) → `cc ≥ 4 cm`; deniz kıyısı → `cc ≥ 5 cm`.
- `d'` ≈ net örtü + 2 cm.

**Malzeme sınıfı sınırı:** min beton `C25/30` (TBDY; ek öneri `C30/37`), max `C80/95`; boyuna/sargı çeliği `B420C`, `B500C`.

**`lb` bağlantısı (kod):** TS500 9.1.2.1 → `lb = max(0.12·fyd/fctd·φ, 20φ)`. C30/37 + B420C için `fyd=365.2`, `fctd=1.2667` → `34.60φ` (konum II); konum I ×1.4 → **`48.44φ`**. Kodda: `KirisAcilimLbPhiKonumI=48.44`, `KirisAcilimLbPhiKonumII=34.60`.

---

## 4. Hesap formülleri (özet)

Sabitler: `k1=0.82`, `k3=0.85`, `εcu=0.003`, `Es=2·10^5 N/mm²`, `fyd=420/1.15=365.22`.
`ρb=0.0237` (C30/37-B420C), `0.85ρb=0.0201`, `min ρ=0.8 fctd/fyd`, `max ρ=0.02`.

**Tek donatılı dikdörtgen:**
```
K = 2Md / (k3 fcd bw d²)          (birimsiz)
a = d(1 − √(1−K))                 → 0<a<d olmalı, −'li kök
As = k3 fcd a bw / fyd
```
**Çift donatılı:** `M1 = 0.235 fcd bw d²(1 − 0.1175/k3)`; `M1<Md` ise
```
As1 = 0.235 fcd bw d / fyd
M2  = Md − M1 ,  As2 = M2 / (fyd (d−d'))
ε's = 0.003(1 − (k1/k3)·(0.235 d/d − d'/d))   → σ's = Es ε's; σ's>fyd ise σ's=fyd
As = As1+As2 ,  A's = As2 fyd/σ's
```
**Tablalı kesit:** `M = k3 fcd t b (d − t/2)`; `M ≥ Md` ise `a ≤ t` (dikdörtgen gibi, genişlik `b`), `M < Md` ise `a > t` (kulak + gövde iki model).

**Tablolar:** `K=10Md/(bw d²)` [N/mm²] → tablodan `ω` → `As = ω fcd b d / 10⁴ / fyd`. `ρ = ω/10⁴`.
Standart aralıklı döşemede: `K=10Md/(1000·d²)`.

**Pratik ön tahmin:** `As ≈ Md / (0.96 fyd d)` (dikdörtgen ve tablalı, açıklık ve mesnet).

**Çalışan (etkili) tabla genişliği:** `b ≤ bw + 12t` **ve** `b ≤ bw + 0.2 α Lh` (α: basit=1, sürekli=0.8). Tabla **açıklıkta** basınç bölgesinde çalışır, **mesnette çekme** bölgesinde olduğundan çalışmaz (mesnette kesit dikdörtgen modellenir).

**Örnek malzeme sabitleri**

| Sınıf | fcd | fctd | fyd | ρb | 0.85ρb |
|---|---|---|---|---|---|
| C20/25-B420C | 13.33 | 0.93 | 365.22 | – | – |
| C25/30-B420C | 16.67 | 1.20 | 365.22 | 0.0205 | 0.0174 |
| C30/37-B420C | 20.00 | 1.28 | 365.22 | 0.0237 | 0.0201 |
| C35/45-B420C | 23.33 | 1.40 | 365.22 | 0.0267 | 0.0227 |

> Not: PDF'te bir örnekte `C20/25` başlığı altında `fcd=23.33` verilmiş; bu değer **C35/45**'e aittir (kaynaktaki etiket karışıklığı).

---

## 5. Kiriş açılımı (şantiyeye giden donatı çizimi) kuralları — sf. 176

1. **Ölçek 1/20.** Kirişin **boyuna** ve **en az bir enine kesiti** çizilir.
2. **Her çubuğun açılımı kirişin ALTINA** çizilir. Çubuğun **kanca, yatay boyu, toplam boyu, adet ve çapı** çizginin üzerine yazılır (ör. `3Ø18 L=7200`, `2Ø12 L=6520 gövde`, `Etr. Ø8/100 L=1860`).
3. **Çubuk açılım sırası (yukarıdan aşağıya):**
   1. Mesnet üst ek donatıları
   2. Açıklık üst donatıları (montaj)
   3. Gövde donatıları
   4. Açıklık alt donatıları
   5. Mesnet alt ek donatıları
4. **Kenar mesnet:** boyuna donatılar kenetlenme için **90° bükülür** ve kiriş derinliğince uzatılır (`a ≥ 0.4lb`, `b ≥ 12φ`, `a+b ≥ lb`).
5. **Kesilen çubuklar komşu açıklığın `Ln/4`'üne kadar** uzatılır (`c`).
6. Kalın ve nervürlü boyuna donatıda **135° kanca yapılmaz**; etriyeler **135° kıvrımlı kancalı**.
7. **Etriye:** açıklıkta `φ8/200`, sarılma bölgelerinde `φ8/100`; 20 cm'den seyrek kullanılmaz; aralık `h/2`'den fazla olamaz; `ρw = Asw/(s·bw) ≥ 0.3 fctd/fywd`.
8. **Donatılar elden geldiğince KESİLMEZ** (kenetlenme iyileşir, mesnette yığılma önlenir, işçilik azalır). Çubuk boyu 12 m'yi aşmıyorsa boydan boya uzatılır.
9. **Pilye** yapılıp yapılmayacağına ve çubukların kesilip kesilmeyeceğine karar verilir.
10. Şu iki koşul **hem mesnette hem açıklıkta** sağlanır:
    - **Montaj (açıklık üst) donatısı ≥ mesnet donatısı / 4**
    - **Mesnet alt donatısı ≥ mesnet üst donatısı / 2**
11. **Gövde donatısı** `h>60 cm` ise (`Asgövde ≥ 0.001 bw d`, aralık ≤ 30 cm); `h ≤ 60` ise gerekmez.
12. **Hesap sırası:** önce açıklık, sonra mesnet donatıları (açıklıktan mesnete donatı gelir). Momentleri ≈%10 yakın açıklıklar için tek hesap; önce büyük moment hesaplanır.
13. Çubuklar kesite sığmazsa: çap büyüt, kesiti genişlet veya **çift sıra** koy.
14. Örnek kiriş (250×700, Ln=5400, 300×600 kolonlar, C25/30-B420C): `3Ø18 L=7200` alt, `3Ø12 L=7200` üst montaj, `2Ø12 L=6520 gövde`, `4Ø18 L=2250` (üst ek, tablaya 2 tanesi), `Etr. Ø8/100 L=1860` (sık) / `Ø8/200 L=1860` (orta).

---

## 6. Kod karşılıkları (izleme)

| Referans | Bizim kod / sabit |
|---|---|
| `Ln/4` ilave uzatması | `KirisAcilimIlaveYuzdenCm` — yüzden Ln/4 |
| `50φ` (hk+c) | `KirisAcilimUstIlavePhi = 50`; mesnet genişliği düşünce artan kısım |
| `lb_I` konum I | `KirisAcilimLbPhiKonumI = 48.44` |
| `lb_II` konum II | `KirisAcilimLbPhiKonumII = 34.60` |
| `d + 20φ` kesim aşımı | ilave boyuna eklenmez; c = Ln/4 |
| montaj ≥ `ρ1/4` | `KirisAcilimUstSurekliOran` (montaj min oranı) |
| konum II üst sınırı (serbest üst yüzden) | `KirisAcilimKonumIIUsttenCm = 30` |
| ölçü 5 cm yuvarlama | `KirisAcilimYuvarla` |
| etriye bölgeleri (uç sık/orta seyrek) | KOLONDUSEY / KIRISACILIMLARI etriye açılımı |

---

## 7. Bizim için açık/uygulanacak başlıklar

- [ ] Kiriş açılımında **çubuk sıralaması** (kb. §5.3) çizimimizde aynı mı?
- [ ] Etiket formatı: `nØφ` + `L=` + gerekirse `mon.` / `gövde` / `düz` (STA uyumu).
- [ ] Kenar mesnette **90° büküm** ve `Ln/4` uzatma çizimi.
- [ ] `h>60 cm` kirişlerde gövde donatısı çizimi.
- [ ] Etriye L (açılım boyu) hesabı: `2(bw−2cc)+2(h−2cc)` + kanca; örnek `L=1860` (250×700).
- [ ] Montaj ≥ mesnet/4 ve mesnet altı ≥ mesnet üstü/2 **kontrol/uyarı**.
