# Kolon Donatı Tablosu DXF – Detaylı İnceleme

Bu belge `KOLON_DONATI_TABLOSU.dxf` dosyasının yapısını, katmanları, ölçüleri ve metin stillerini özetler. DLL ile tabloyu bire bir oluşturmak için referans alınacaktır.

---

## 1. Genel Bilgiler

- **Dosya:** KOLON_DONATI_TABLOSU.dxf  
- **ACADVER:** AC1032 (AutoCAD 2018+)  
- **Kod sayfa:** ANSI_1254 (Türkçe)  
- **Çizim sınırları (HEADER):**
  - **EXTMIN:** X=115.75, Y=50.99  
  - **EXTMAX:** X=1736.52, Y=811.72  
- **Ölçek:** DIMSTYLE "sta" içinde **DIMLFAC = 5000.0** (ölçü çarpanı). Tablo koordinatları muhtemelen **çizim birimi = 1 birim** (ileride cm/mm netleştirilebilir).

---

## 2. Katmanlar (LAYER) – Tabloda Kullanılanlar

| Katman adı | Açıklama / Kullanım | Renk (62) | Çizgi tipi |
|------------|----------------------|-----------|------------|
| **donatiplus.com** | Tablo çerçevesi ve hücre çizgileri (LINE) | 7 (beyaz/siyah) | Continuous |
| **PENC4** | Başlık satırı metinleri: "Kat no", "1. NORMAL", "2. NORMAL", "3. NORMAL" ve satır etiketleri: "S1", "S2", "S3" | 7 | Continuous |
| **YAZI (BEYKENT)** | Genel sayısal/görünen metin (ebad, kot sayıları vb.) | 140 | Continuous |
| **SUBASMAN (BEYKENT)** | Bodrum / özel kat sayıları (örn. 70) | 197 | Continuous |
| **KOT (BEYKENT)** | Kot değerleri (örn. -55) | 7 | Continuous |
| **KOLON ISMI (BEYKENT)** | Kolon kesit adı (örn. 60/60) | 91 | Continuous |
| **KIRIS ISMI (BEYKENT)** | Kiriş etiketi (örn. 15) | 40 | Continuous |
| **DONATI YAZISI (BEYKENT)** | Donatı metni (örn. 2x4ø16+2x4ø16, ø10/20/20) | 3 | Continuous |

**Renk geçersiz kılma (entity 62):**  
- **62 = 251:** İnce ayırıcı çizgiler (yatay, hücre içi)  
- **62 = 252:** Sol sütundaki küçük başlık metinleri ("ebad", "donati", "etriye") – bu metinler katman **donatiplus.com** üzerinde, rengi 252.

---

## 3. Metin Stilleri (STYLE)

| Stil adı | Font | Varsayılan yükseklik | Genişlik çarpanı (41) | Kullanım |
|----------|------|----------------------|------------------------|----------|
| **YAZI (BEYKENT)** | Bahnschrift Light Condensed | 0 (entity’de verilir) | 1.0 | Tablo metinleri (sayı, kot, kolon/kiriş adı vb.) |
| **DONATI YAZISI (BEYKENT)** | Bahnschrift Light Condensed | 0 | 1.0 | Donatı ifadeleri (2x4ø16+2x4ø16, ø10/20/20) |
| **KLNOLC-25** | Bahnschrift Light Condensed | 10.0 | 0.75 | Ölçü stili "sta" ile ilişkili (DIMSTYLE) |

Tablodaki TEXT entity’lerde:  
- **40 (height):** 11.5 (ana metin) veya 9.0 (küçük başlık: ebad, donati, etriye)  
- **41 (width factor):** 0.7  
- **7 (style):** Çoğunlukla "YAZI (BEYKENT)"; donatı hücrelerinde "DONATI YAZISI (BEYKENT)".

---

## 4. Tablo Geometrisi – Koordinatlar (Drawing Units)

Tüm koordinatlar **Z = 0**. Değerler DXF’ten okunan gerçek sayılardır (yaklaşık 1136.52… vb.).

### 4.1 Dikey çizgiler (sütun sınırları) – X değerleri

| Sıra | X (sol) | Sütun genişliği (birim) |
|------|---------|--------------------------|
| 0 | 1136.521324442871 | — |
| 1 | 1186.521324442871 | 50 |
| 2 | 1286.521324442871 | 100 |
| 3 | 1436.521324442871 | 150 |
| 4 | 1586.521324442871 | 150 |
| 5 | 1736.521324442871 | 150 |

**Toplam tablo genişliği:** 600 birim (1736.52 − 1136.52).

**Sütun anlamları:**  
- **1. sütun (50):** "Kat no" başlığı + satır etiketleri (S1, S2, S3) + sol alt hücrede "ebad", "donati", "etriye" (küçük font).  
- **2. sütun (100):** Ortak başlık alanı (ebad / donati / etriye satırları bu sütunda devam eder; yatay çizgilerle ayrılır).  
- **3.–5. sütunlar (150’şer):** "1. NORMAL", "2. NORMAL", "3. NORMAL" – her birinde: ebad, kot, kolon ismi, kiriş ismi, donatı satırları.

### 4.2 Yatay çizgiler (satır sınırları) – Y değerleri

Yukarıdan aşağıya (Y azalıyor):

| Y (yaklaşık) | Açıklama |
|--------------|----------|
| 799.7965632368764 | Tablo üst kenarı |
| 749.7965632368764 | Başlık satırı altı ("Kat no" / "1. NORMAL" …) |
| 724.7965632368764 | "ebad" satırı altı |
| 699.7965632368764 | "donati" satırı altı |
| 674.7965632368764 | "etriye" satırı altı → Bölüm S1 sonu |
| 599.7965632368764 | Bölüm S2 sonu / S3 üstü |
| 574.7965632368764 | (iç yatay) |
| 549.7965632368764 | (iç yatay) |
| 524.7965632368764 | Bölüm S3 sonu (tablo alt kenarı örneği) |

**Sabit farklar (birim):**  
- Başlık satırı yüksekliği: 50 (799.79 → 749.79).  
- Her "bölüm" (S1, S2, S3) 3 alt satır: 25 + 25 + 25 = 75 birim (724→699→674, 599→574→549→524).

**Özet:**  
- **Başlık:** 1 satır, yükseklik 50.  
- **Veri bölümü:** Her bölüm 3 satır (ebad / donati / etriye), satır yüksekliği 25.  
- Tablo en altında Y = 524.79… (örnek); daha aşağı satırlar varsa DXF’te tekrarlanır.

---

## 5. Hücre İçerikleri ve Katman Eşlemesi

- **PENC4:** "Kat no", "1. NORMAL", "2. NORMAL", "3. NORMAL", "S1", "S2", "S3".  
- **donatiplus.com (62=252):** "ebad", "donati", "etriye" (9.0 yükseklik).  
- **YAZI (BEYKENT):** Sayılar (35, 55, 15 vb.).  
- **SUBASMAN (BEYKENT):** Bodrum kat sayısı (70).  
- **KOT (BEYKENT):** Kot (-55 vb.).  
- **KOLON ISMI (BEYKENT):** Kesit (60/60).  
- **KIRIS ISMI (BEYKENT):** Kiriş no (15).  
- **DONATI YAZISI (BEYKENT):** "2x4ø16+2x4ø16", "ø10/20/20", "ø10/20/9" vb.

Metin yerleşimi: **10, 20** = ekleme noktası (sol alt); **40** = yükseklik; **1** = içerik; **7** = text style; **8** = katman.

---

## 6. Ölçü Stili (DIMSTYLE "sta")

- **DIMLFAC:** 5000.0  
- **DIMSCALE:** 0.01  
- **DIMTXTDIRECTION / DIMLUNIT:** Ölçü metni ve birim ayarları mevcut (detay DXF TABLES bölümünde).  
Tablo çizgisi/metni için doğrudan kullanılmıyor; ölçü çizimi için referans.

---

## 7. DLL İçin Özet – Bire Bir Uyarlama

1. **Birim:** Koordinatlar 1136…1736 ve 524…800 aralığında. Birim (cm/mm) projede sabitlenmeli; aynı sayılar kullanılırsa tablo aynı boyutta olur.  
2. **Katmanlar:** Yukarıdaki 8 katman adı ve renkleri (62) aynen kullanılmalı.  
3. **Çizgiler:** Tüm tablo çizgileri **donatiplus.com**; ince ayırıcılar için entity rengi **62=251**.  
4. **Metin:**  
   - Stil: **YAZI (BEYKENT)** veya **DONATI YAZISI (BEYKENT)**  
   - Height: 11.5 (normal), 9.0 (ebad/donati/etriye)  
   - Width factor: 0.7  
   - Font: Bahnschrift Light Condensed (yoksa projede tanımlı eşdeğer).  
5. **Sütun genişlikleri:** 50, 100, 150, 150, 150 (toplam 600).  
6. **Satır yükseklikleri:** Başlık 50; her veri bölümü 3×25 = 75.  
7. **Hücre içeriği:** Katman–içerik eşlemesi yukarıdaki tabloya göre atanmalı.

---

## 8. Netleştirilmesi Gerekenler (Sizinle)

1. **Çizim birimi:** Tablo çiziminde 1 birim = 1 cm mi, 1 mm mi, yoksa başka bir ölçek mi kullanılsın? (DLL’de insert noktası ve ölçek buna göre ayarlanacak.)  
2. **"ebad", "donati", "etriye":** Bu metinler her zaman sabit mi kalacak, yoksa dil/versiyona göre değişecek mi?  
3. **S1, S2, S3:** Bu etiketler sabit mi; yoksa bölüm sayısı (kolon sayısı / kat sayısı) artınca S4, S5… üretilecek mi?  
4. **"1. NORMAL", "2. NORMAL", "3. NORMAL":** Sütun sayısı ST4’ten mi gelecek (kaç kat = kaç "NORMAL" sütunu)?  
5. **Font:** Bahnschrift Light Condensed sistemde yoksa yerine hangi font kullanılsın (örn. Arial, RomanSTA)?

Bu belge, DLL’de kolon donatı tablosunu aynı ölçü ve katmanlarla oluşturacak komut için temel referanstır. Soruları yanıtladıktan sonra komut mantığı (sütun/satır sayısı, veri kaynağı) netleştirilip kod taslağı çıkarılabilir.
