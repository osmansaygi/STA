# GPR: Döşeme betonarme hesap sonuçları — yapı ve çıkarma kuralları

Bu belge, Statik çıktısı **`.GPR`** dosyalarında **“DÖŞEME BETONARME HESAP SONUÇLARI”** tablolarının nasıl konumlandığını ve projeler arasında **tutarlı şekilde parse** edilmesi için kuralları tanımlar. Örnekler: `SB_EMN_01.GPR`, `SL_09.GPR`, `GI_B2_01.GPR`, `YP_OTP_08.GPR`.

---

## 0. Dosya içi kesin sınır (tüm projeler — birincil kural)

GPR dosyasında **döşeme betonarme hesap dökümü** (sayfa tekrarları dahil) şu iki metin arasındadır:

| | Metin (UTF-8 doğru) | Bozuk encoding örneği (1254 yanlış okununca) |
|---|---------------------|-----------------------------------------------|
| **Başlangıç** | İlk geçen **`  DÖŞEME BETONARME HESAP SONUÇLARI`** (başta iki boşluk olabilir) | `  DEME BETONARME HESAP SONULARI` vb. |
| **Bitiş** | Bu başlangıçtan **sonra** dosyada ilk geçen **`KİRİŞ VE PANEL BİLGİLERİ`** | ` KR VE PANEL BLGLER` vb. |

**Kurallar:**

1. **Başlangıç:** Dosyada **ilk** `DÖŞEME BETONARME HESAP SONUÇLARI` eşleşmesi (genelde hemen üstünde `130,150,0,0,-10,1` satırı vardır).
2. **Bitiş:** Aynı dosyada, bu başlangıçtan sonra gelen **ilk** `KİRİŞ VE PANEL BİLGİLERİ` satırı — bu satır **bir sonraki bölümün başlığıdır**; döşeme tablo içeriği pratikte **bu satırdan önce** biter (bitiş satırı döşeme verisine dahil edilmez).
3. Aynı başlık (`DÖŞEME BETONARME…`) tablo **sayfa sayfa** tekrarlanabilir; hepsi yine **tek sürekli aralık** olarak bu sınırlar içinde kalır.
4. **`KİRİŞ BETONARME HESAP SONUÇLARI`** bu sınırın parçası **değildir**; o başlık genelde daha sonra gelir. Kesin kesit için **bitiş anchor’u `KİRİŞ VE PANEL BİLGİLERİ` kullanılmalıdır.**

Parse / dilimleme için öneri: satır dizisinde `startIndex` = ilk döşeme başlık satırı, `endIndex` = ilk `KİRİŞ VE PANEL BİLGİLERİ` satırının indeksi; dilim `[startIndex, endIndex)` veya başlık satırı hariç gövde için uygun alt aralık.

---

## 1. Kodlama (önkoşul)

- GPR metinleri çoğu projede **Windows-1254 (Türkçe)** ile kayıtlıdır. Editörde “bozuk Türkçe” (`DEME`, `Sonular`) görüyorsanız önce `Convert-GprToUtf8.ps1` ile UTF-8’e çevirin veya okurken **1254** decode kullanın.
- Parse mantığında başlık eşlemesi için **normalize** edilmiş string kullanın: `BETONARME` + `HESAP` + `SONU` (son harf `LARI` / `LARI` encoding farkları için esnek).

---

## 2. Bölüm kimliği (anchor)

### 2.1 Sabit ön ek satırı

Tablo başlığı (büyük punto) hemen önce şu **sabit format** gelir:

```text
130,150,0,0,-10,1
```

- Anlam (çıktı koordinat/font ile ilgili): `130` = X, `150` = Y, `-10` muhtemel font/yükseklik kodu, sondaki `1` bayrak.
- **Kural:** `130,150,0,0,-10,1` satırından **sonraki satır** döşeme betonarme başlık metnidir.

### 2.2 Başlık metni (bir satır)

UTF-8 düzgün dosyada:

```text
  DÖŞEME BETONARME HESAP SONUÇLARI
```

- Başta **iki boşluk** olabilir.
- **Arama:** Büyük/küçük harf duyarsız; `DÖŞEME` yerine bozuk encoding’de `D.*EME` veya sadece `BETONARME HESAP` ile eşleştirme güvenlidir.

### 2.3 Tekrarlayan başlık (sayfa devamı)

Uzun projelerde tablo **birden çok sayfaya** bölünür. Her yeni sayfada aynı çift tekrarlanır:

1. Sayfa çerçevesi / `SAYFA: n` bloğu (aşağı §4)
2. Yine `130,150,0,0,-10,1` + `  DÖŞEME BETONARME HESAP SONUÇLARI`

**Kural:** Aynı başlıkla **birden fazla “sayfa parçası”** olabilir; hepsi §0’daki **tek dosya aralığı** içinde kalır (baş: ilk döşeme başlığı, son: ilk `KİRİŞ VE PANEL BİLGİLERİ` öncesi).

---

## 3. Tablo iç satırlarının formatı

### 3.1 Koordinatlı satır ön eki

Başlık ve tablo gövdesi satırları şu kalıpta gelir:

```text
130,<Y>,0,0,8,1
```

- **Y** değeri satır yüksekliği ile birlikte artar; örnek dizilim: `180, 210, 240, 270, 300, 330, 360, ...` (çoğu örnekte **30 birim** adım).
- `8` = tablo gövdesi font/yükseklik kodu (başlıktaki `-10`’dan farklı).

**Kural:** Döşeme satırlarını toplarken `130,*,0,0,8,1` ile başlayan satırların **bir sonraki satırı** gerçek metindir (çoğu GPR düzeninde metin ayrı satırda).

### 3.2 Tablo çerçeve ve ayırıcı satırları

- Üst/alt çerçeve: `ÚÄÄÄ...`, `ÃÄÄÄ...` (DOS/CP437 tarzı) veya Unicode kutu çizgileri (`┌─┐│`).
- Satır ayırıcı: yine kutu çizgisi karakterleri ile dolu satırlar.
- **Kural:** Bu satırlar **veri değildir**; parse’te atlanır veya “separator” olarak işaretlenir.

### 3.3 Sütun başlıkları (sabit anlam)

Düzgün metinde iki satır başlık vardır (önce kavramsal isimler, sonra birimler):

| Kavram (satır 1) | Birim (satır 2) |
|------------------|-----------------|
| Döşeme no | (no) |
| Msol | (tm) |
| As | cm² |
| Maç | (tm) |
| As | cm² |
| Msağ | (tm) |
| As | cm² |
| Donatı | (serbest metin) |

- Bazı dosyalarda `¡` veya `|` sütun ayırıcı olarak kullanılır (`¡Döşeme  ¡  Msol  ¡`).

---

## 4. Sayfa sonu / sayfa başı (devam tabloları için)

Sayfa koptuğunda tipik blok:

```text
90,10,95
100,10,1970,10,0,-1,10,0,0
...
20,2570,0,0,0,-3,0,4,101
140,30,0,0,-10,1
FİRMA : ...
SAYFA: 90
...
110,2820,0,8,5,1
@^#.........
130,150,0,0,-10,1
  DÖŞEME BETONARME HESAP SONUÇLARI
```

- `90,10,95` veya `91,10,95` gibi ilk satır sayfa boyutu ile ilgili meta.
- `SAYFA: <numara>` insan okuması için sayfa numarası.
- `@^#` ile başlayan satır genelde “font/şifreleme” içeren özet satırı.

**Kural:** Sayfa sonu **tabloyu bitirmez**; sadece çıktıyı keser. Veri birleştirme: tüm sayfalardaki `130,...,8,1` metin satırları sırayla birleştirilir.

---

## 5. Döşeme veri satırları (asıl içerik)

### 5.1 Her döşeme için iki yön satırı

Tipik yapı:

1. **X yönü** (ilk satır): döşeme kimliği + `X` + momentler + alanlar + **donatı özeti** (en sağda).
2. **Y yönü** (ikinci satır): **`d=<kalınlık>cm`** + `Y` + aynı sütun düzeni.

Örnek (`SL_09.GPR`, UTF-8):

```text
¡DB-01  X¡    0.00¡   0.00¡    2.26¡   3.74¡    0.00¡   0.00¡ ø8/26(düz)+ø8/26(pil)                       ¡
¡d=20cm Y¡    0.00¡   0.00¡    2.22¡   3.67¡    0.00¡   0.00¡ ø8/27(düz)+ø8/27(pil)                       ¡
```

**Kural:**

- **Kalınlık** pratikte **Y satırında** `d=\d+cm` ile verilir; X satırında yoktur.
- **Donatı metni** satırın sağında; `+` ile birleştirilmiş bölgeler: `(düz)`, `(pil)`, `(sol ila)`, `(sağ ila)` vb.

### 5.2 Döşeme kimliği (etiket) deseni

Projelerde önekler:

- `DB-01` … kat / blok kodu **DB**
- `DZ-01`, `D1-01`, `D2-01`, `DC-01` … farklı kat veya blok grupları

**Önerilen regex (ham metin):**

```regex
(?m)^[^\w]*([A-Z]{1,2}\d?-\d+)\s+[XY]
```

veya satır başındaki gürültüyü temizledikten sonra:

```regex
\b([A-Z]{1,2}\d?-\d{1,3})\b
```

**Kural:** Aynı kimlik için **önce X sonra Y** (veya dosyada gösterilen sıra) çifti bir kayıt oluşturur.

### 5.3 Sayısal alanlar

- Ondalık ayırıcı: **nokta** `.`
- Moment birimi başlıkta `(tm)`; alan `cm²` için `As` sütunları.

Parse için: X satırından ve Y satırından ilgili sütunları **sabit sütun genişliği** veya `¡` / `|` ayraçlarına göre **split** etmek gerekir; farklı GPR sürümlerinde boşluk hizası kayabilir — **ayıraçlı split** daha güvenilir.

---

## 6. Bölümün bittiği yer (§0 ile uyumlu)

**Birincil bitiş işareti:** §0’da tanımlandığı gibi dosyada **ilk** `KİRİŞ VE PANEL BİLGİLERİ` satırı. Bazı projelerde aynı metin **birden fazla** geçebilir; döşeme dökümü için **başlangıçtan sonraki ilk** geçiş kullanılır.

**Sonrasında gelen diğer bölümler** (döşeme aralığının dışında): örneğin `KİRİŞ BETONARME HESAP SONUÇLARI`, `KOLON BETONARME…`, `TEMEL…` — bunlar **kesit sınırı değildir**; sadece dosya sırası bilgisidir.

**Önerilen çıkarma algoritması (özet):**

1. Dosyayı satır listesi olarak oku (encoding: 1254 veya UTF-8).
2. `start` = ilk **`  DÖŞEME BETONARME HESAP SONUÇLARI`** (normalize eşleşme ile).
3. `end` = `start`tan sonra ilk **`KİRİŞ VE PANEL BİLGİLERİ`**.
4. `[start, end)` aralığındaki satırlarda tablo gövdesini parse et (`130,*,0,0,8,1` + metin satırları; §3–5).

---

## 7. Diğer projelerle karşılaştırma (gözlemler)

| Özellik | SL_09 | GI_B2_01 | YP_OTP_08 | SB_EMN_01 |
|--------|-------|----------|-----------|-----------|
| Başlık anchor | `130,150,0,0,-10,1` | Aynı | Aynı | Aynı |
| Gövde satırı | `130,Y,0,0,8,1` | Aynı | Aynı | Aynı |
| Türkçe karakter | Düzgün (UTF-8) | Düzgün | Düzgün | Bozuksa 1254 dönüşümü |
| Donatı çap simgesi | `ø` | `ø` | `ø` | Bozukta ``, `?` vb. |

**Kural:** Görsel rapor aynı motorla üretildiği için **satır ön ekleri sabittir**; farklılık çoğunlukla **encoding** ve **tablo uzunluğu** (sayfa sayısı) düzeyindedir.

---

## 8. Özet kontrol listesi (implementasyon)

- [ ] Dosya kodlamasını tespit et (1254 ↔ UTF-8).
- [ ] Bölüm başı: `130,150,0,0,-10,1` + başlık metni.
- [ ] Veri satırları: `130,<y>,0,0,8,1` + bir alt satırda metin.
- [ ] Çerçeve/ayırıcı satırlarını at.
- [ ] Her döşeme: X satırı + `d=...cm` içeren Y satırı.
- [ ] Kimlik: `[A-Z]{1,2}\d?-\d+`.
- [ ] Bölüm sonu (kesin): `start`tan sonra ilk `KİRİŞ VE PANEL BİLGİLERİ` (§0).
- [ ] İsteğe bağlı: `SAYFA: n` ile sayfa numarasından rapor sayfasına geri iz sürme.

---

## 9. İlişkili dokümanlar

- `docs/doseme-etiket-veri.md` — ST4 döşeme etiketi (kat planı) ile alan eşlemesi.
- `Convert-GprToUtf8.ps1` — GPR/PRN Türkçe karakter düzeltmesi.

## 10. KALIP50 entegrasyonu (kabuk)

- **Kod:** `GprDosemeDonatiParser.TryParse` → `GprDosemeDonatiXy` (X ve Y ayrı), `PlanIdDrawingManager.DrawKalipDonatiPlanGprSlabNotes`.
- **GPR satır biçimi:** Tablo satırları çoğu zaman `¡` (ters ünlem) ile başlar (`¡DZ-01  X¡`). Regex eşlemesi bu karakter **kaldırıldıktan sonra** yapılır; aksi halde hiçbir döşeme anahtarı okunmaz.
- **Kodlama:** STA4 GPR dosyaları çoğunlukla **Windows-1254** veya **ISO-8859-9** kayıtlıdır; yalnızca UTF-8 ile okunursa `¡` ve Türkçe karakterler bozulur, sözlük boş kalır. Parser **UTF-8, 1254, ISO-8859-9** ile dener; **en çok döşeme anahtarı** üreten sonuç seçilir. Gerekirse `Convert-GprToUtf8.ps1` ile UTF-8’e çevirin.
- ST4 ile **aynı klasör ve aynı taban ad**lı `.GPR` / `.gpr` okunur; **donatı planı (sağ kopya)** üzerinde döşeme **poligon merkezine** iki satır: **X yönü** metni **ACI 1 (kırmızı)**, **Y yönü** metni **ACI 5 (mavi)**; katman `DONATI YAZISI (BEYKENT)`.
- Eş anahtar: `D` + kat kodu + `-` + numara (örn. `DZ-01`, `DB-12`). ST4 kısa adı `Z-` gibi biterse sondaki tire atılır, GPR ile aynı `DZ-01` üretilir. İnce ayar ve kurallar sonra netleştirilecek.

## 11. Donatı hücresi parçaları (düz / pilye / montaj / sol–sağ ilave)

KOLONDATA’daki kolon donatı ayrımına paralel olarak döşeme GPR hücresinde **`+` ile birleşen** ve parantezle türü belirtilen **beş ana kategori** için ayrıntılı şema ve örnekler:

- **`docs/gpr-doseme-donati-parcalari.md`**
- Kod: `DosemeDonatiParcalari.SplitDonatiCell`, `DosemeDonatiKind`

Bu kurallar, döşeme donatı planına metin aktarımı veya tablo oluşturma için **GPR tarafından deterministik veri çıkarma** temelidir; AutoCAD çizimi için ek yerleşim kuralları ayrıca tanımlanır.
