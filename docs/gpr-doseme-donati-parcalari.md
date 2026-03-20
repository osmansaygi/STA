# GPR döşeme donatı hücresi — parça türleri (KOLONDATA benzeri şema)

KOLONDATA için **kolon kimliği** ve **GPR satırından donatı okuma** `docs/kolon-donati-tablosu-komut-spec.md` içinde tanımlıdır. **Döşeme** tarafında eşleşme **döşeme adı** ile yapılır (ST4 etiketi `D` + kat kısa adı + numara, örn. **DB-01**, **DB-001**, **DB-20**); GPR satırında aynı kod **X** ve **Y** satırlarında tekrarlanır.

---

## 1. Satır yapısı (her döşeme)

| Satır | Anlam | Örnek baş |
|--------|--------|-----------|
| **X** | Birinci yön (genelde eksen çiftinin “X” yönü) | `¡DB-001  X¡` … veya bozuk encoding’de `DB-001  X` |
| **Y** | İkinci yön; satır başında **kalınlık** | `¡d=20cm Y¡` … veya `d=12cm Y` |

Donatı metni satırın **son sütununda** (GPR’de genelde `¡` ayraçlarıyla); ham hücre `KolonDonatiTableDrawer.ExtractGprDonatiCellFromLine` ile okunur.

---

## 2. Hücre içi biçim: `+` ile birleşen parçalar

Donatı hücresi **birden fazla** donatı ifadesinden oluşur; araya **`+`** konur (boşluksuz veya boşluklu):

```text
ø10/20(düz)+ø10/30(sol ila)+ø10/21(sağ ila)
```

Her **parça** tipik olarak:

```text
<çap/aralık/üstyüzey ifadesi>(<tür kısaltması>)
```

Tür, **son parantez çiftindeki** metinle belirlenir: `(düz)`, `(pil)`, `(Mon.)`, `(sol ila)`, `(sağ ila)` vb.

---

## 3. Beş donatı yazısı türü (sonek / anlam)

Statik çıktıda aynı mantıkta tekrarlanan **5 ana kategori** (parantez içi metin, projede küçük yazım farkları olabilir):

| Tür | Parantez örnekleri | Anlam |
|-----|-------------------|--------|
| **1 — Düz donatı** | `(düz)`, bozuk encoding `(dz)` | Ana yön düz donatı |
| **2 — Pilye** | `(pil)` | Pilye donatısı |
| **3 — Montaj** | `(Mon.)`, `(mon.)` | Montaj donatısı |
| **4 — Sol ilave** | `(sol ila)`, `(sol ila)` ile boşluk | Sol ilave donatı |
| **5 — Sağ ilave** | `(sağ ila)`, `(sag ila)` | Sağ ilave donatı |

**Not:** Türkçe **ı/ğ/ş/ü** ve `¡` / UTF-8 bozulması nedeniyle parser’da **içerik normalize** edilip `Contains` / küçük harf eşlemesi kullanılır (`DosemeDonatiParcalari` sınıfı).

---

## 4. Örnekler (referans satırlar)

### A_AKD_D1.GPR — **DB-001**

- **X yönü** (tek hücre):  
  - `ø10/20(düz)` → **düz**  
  - `ø10/30(sol ila)` → **sol ilave**  
  - `ø10/21(sağ ila)` → **sağ ilave**

- **Y yönü**:  
  - `ø10/40(düz)` → **düz**  
  - `ø10/40(pil)` → **pilye**  
  - `ø16/30(sol ila)` → **sol ilave**

### GI_A_01.GPR — **DB-20**

- **X yönü**:  
  - `ø8/18(düz)` → **düz**  
  - `ø10/20(Mon.)` → **montaj**

- **Y yönü**:  
  - `ø8/36(düz)` → **düz**  
  - `ø8/36(pil)` → **pilye**

---

## 5. X ve Y ayrımı (çizim / etiket)

- **Okuma:** GPR’den ayrı ayrı **X satırı donatı hücresi** ve **Y satırı donatı hücresi** alınır; birleşik tek metin yerine ileride **yön bazlı** gösterim için saklanabilir.
- **KALIP50 / planda yazdırma:** Şu an kabukta `X metni | Y metni` birleşimi kullanılabilir; ayrıntılı etiket yerleşimi ve çok satır kuralları sonra netleştirilir.

---

## 6. İlişkili kod

- `GprDosemeDonatiParser` — GPR dosya aralığı ve X/Y satır eşleştirmesi.
- `DosemeDonatiParcalari` — Hücre string’ini `+` parçalarına böler; parantezden **tür** çıkarır (`ST4_Plan_ID_Ciz`).

---

## 7. Kolon tarafı ile fark

| | Kolon (KOLONDATA) | Döşeme |
|---|-------------------|--------|
| GPR kimlik | `SB-01`, `S1-02` … | `DB-01`, `DZ-05`, `DB-001` … |
| ST4 eşlemesi | `S` + kat kısa adı + `-` + no | `D` + kat kısa adı + no (etiket) |
| Satır | Tek satırda ebat/donatı/etriye (kolon tablosu) | **X** ve **Y** iki satır; donatı `+` parçaları |

Bu belge, döşeme donatı metninin **anlamsal parçalanması** için referanstır; görsel yerleşim kuralları ayrıca tanımlanır.
