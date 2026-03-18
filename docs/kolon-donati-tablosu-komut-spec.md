# Kolon Donatı Tablosu – Komut Özellik Şartnamesi

Bu belge, DLL’e eklenecek **kolon donatı tablosu** komutunun veri kaynakları, tablo yapısı ve çizim kurallarını tanımlar.

---

## 1. Komut Adı ve Genel Kurallar

- **Komut adı:** **KOLONDATA**
- **Çizim birimi:** CM.
- **Font:** Bahnschrift Light Condensed (tablo metinleri).
- **Yerleşim:** Kullanıcının komutla gösterdiği noktaya tablo çizilir (tek insert noktası).

---

## 2. Veri Kaynakları

- **ST4 dosyası:** Hangi ST4 üzerinde çalışılıyorsa o dosya (örn. `SL_09.ST4`). Komut çalıştığında aktif/seçili ST4 veya aynı isimli ST4 kullanılır.
- **Sütun başlıkları:** **ST4 dosyasından** alınır. ST4’teki **Story** bölümünde katlar **sırayla** tanımlıdır; tablo sütun başlıkları bu **kat isimleri** olur. Örnek: ilk kat adı “BODRUM” ise ilk veri sütunu başlığı “BODRUM”, ikinci kat “ZEMIN” ise ikinci sütun başlığı “ZEMIN”, üçüncü “1. NORMAL” vb. (PRN’deki SB, SZ, S1 gibi önekler veri eşlemesi için kullanılır; başlık metni her zaman ST4’teki kat ismidir.)
- **Ebat / donati / etriye verisi:** Aşağıdaki **dosya arama sırası** ile okunur (her zaman önce GPR):
  1. **GPR:** ST4 ile aynı isimli GPR (örn. `SL_09.ST4` → `SL_09.GPR`). GPR içinde "KOLON BETONARME HESAP SONUÇLARI" bölümü aynı formatta yer alır.
  2. **PRN:** GPR bulunamazsa veya bölüm yoksa aynı isimli PRN (örn. `SL_09.PRN`). “KOLON BETONARME HESAP SONUÇLARI”  3. **İkisi de yoksa:** Kullanıcıya kolon donatı verisi dosyasının yerini sor (dosya seçtir veya yol girilsin).

---

## 3. Sabit ve Değişen Metinler

- **Sol sütun sabit etiketler (tablo dışı / ilk sütun):**
  - **"ebat"** → Sabit. Sağındaki hücreler PRN’den (kolon boyutu: Bx×By veya “Polygon” vb.).
  - **"donati"** → Sabit. Sağındaki hücreler PRN’den (boy donatı ifadesi).
  - **"etriye"** → Sabit. Sağındaki hücreler PRN’den (etriye ifadesi, örn. ø8/15/8(etriye)).

- **Üst başlık:**
  - İlk sütun başlığı: **"Kat no"** (sabit).
  - Diğer sütun başlıkları: **ST4 dosyasındaki** Story bölümünde tanımlı **kat isimleri**, sırayla (ilk kat = ilk sütun başlığı, ikinci kat = ikinci sütun başlığı, …). Örnek: ilk kat “BODRUM”, ikinci kat “ZEMIN”, üçüncü “1. NORMAL” ise başlıklar “Kat no | BODRUM | ZEMIN | 1. NORMAL” olur.

- **Satır etiketleri (S1, S2, S3, …):** ST4 verisine göre üretilir. Her satır = bir “kolon konumu” (eksen kesişimi / kolon no). S1, S2, S3, … şeklinde artan numara; satır sayısı = o projede tanımlı kolon konumu sayısına göre belirlenir.

---

## 4. Tablo Yapısı (ST4 + PRN/GPR’ye Göre)

- **Sütunlar:** ST4’teki **kat listesi** (Story) sırasıyla. Kaç kat varsa o kadar veri sütunu. **Başlık metinleri** doğrudan ST4’teki **kat isimleri** (örn. BODRUM, ZEMIN, 1. NORMAL).
- **Satırlar:** Her satır bir **kolon konumunu** temsil eder (S1, S2, S3, …). ST4’teki kolon pozisyonlarına göre üretilir.
- **Hücre içeriği (kat × kolon kesişimi):** İlgili kat ve kolon için PRN veya GPR’deki **KOLON BETONARME HESAP SONUÇLARI** bloğundan:
  - **ebat:** Bx×By (örn. 35, 70, 60/60) veya Polygon kolon için uygun kısa ifade. **Yuvarlak kolon** ise **"R= " + ÇAP** (örn. R= 50).
  - **donati:** Donatı sütunundaki ana donatı metni **(etriye)** kısmı hariç (örn. 2x4ø16+2x4ø16, 13Ø14).
  - **etriye:** Donatı sütunundaki **(etriye)** kısmı (örn. ø8/15/8, ø10/20/20).

PRN/GPR’de kolon kimliği örnekleri: **SB-01**, **SZ-01**, **S1-02** … (önek = kat kodu, numara = kolon no). **Sadece veri eşlemesi** için kullanılır; tabloda görünen sütun başlıkları her zaman **ST4’teki kat isimleridir** (BODRUM, ZEMIN, 1. NORMAL vb.).

### ST4 kat ↔ GPR kolon kodu (S + kat indisi + kolon no)

GPR’de kolon satırı: **`S` + kat indisi + `-` + kolon numarası** (örn. **SB-46** = S sabit + **B** bodrum + **46** kolon; **SZ-01** zemin; **S1-02** 1. normal; **SC-39** çatı).

**KOLONDATA** eşlemesi öncelikle **ST4 kat kısa adından** türetilir: **GPR = `S` + (tireden önceki kısım)**. Kısa adda tire yoksa tüm kısa ad tek token sayılır (örn. yalnızca **B** → **SB**).

| Kısa ad | GPR önek | Örnek kolon kodu |
|---------|----------|------------------|
| B- veya B | **SB** | SB-01 |
| Z- veya Z | **SZ** | SZ-01 |
| A- veya A | **SA** | SA-01 |
| 1-, 2-, … veya 1, 2, … | **S1**, **S2**, … | S1-01 |
| C- veya C | **SC** | SC-39 |

Kısa ad boşsa kat **adı**ndan tahmin (BODRUM→SB, ZEMIN→SZ, …; **1. NORMAL**→S1).

Donatı sütunu okunurken **S1-01** gibi kolon kodu metni donatı sanılmaz; sütun 9 eksik/bozuksa satırda **(govde)**, **(etriye)**, **ø**, **2×7** vb. içeren hücre aranır.

**Temele inmeyen kolon:** GPR’de yalnız üst kat kodları varsa (örn. yalnız **S1-05**, **SB-05** yok), alt katlarda o kolon için **hücre çizilmez** (GPR’deki en alt kat kodundan daha aşağıdaki katlar atlanır).

- **Sayfa sonu:** Aynı GPR içinde **Panel** olmadan yeni sayfada tekrar **KOLON BETONARME HESAP** başlığı gelirse, parser bu satırda önceki kolon bloğunu kapatır; donatı/etriye karışması önlenir (`a_klc` çok sayfa).
- **Çoklu GPR bölümü:** Panel sonrası veya dosya sonunda tekrar kolon tablosu varsa bloklar birleştirilir; aynı id için **son gelen** geçerlidir.

---

## 5. Çizim Ölçüleri ve Katmanlar (DXF Referansı)

- **Sütun genişlikleri (cm):** 50, 100, 150, 150, 150, … (ilk iki sütun 50+100; sonrakiler kat sayısına göre 150’şer). Toplam genişlik = 50 + 100 + (kat sayısı × 150).
- **Satır yükseklikleri (cm):** Başlık satırı 50; her veri bloğu (S1, S2, …) 3 satır × 25 = 75 (ebat / donati / etriye).
- **Katmanlar (bire bir):**
  - **donatiplus.com** – Tablo çerçevesi ve tüm hücre çizgileri (LINE).
  - **PENC4** – Başlık metinleri (Kat no, 1. NORMAL, …) ve satır etiketleri (S1, S2, S3).
  - **YAZI (BEYKENT)** – Sayısal/görünen metin (ebat sayıları vb.).
  - **SUBASMAN (BEYKENT)** – Bodrum ile ilgili özel değerler.
  - **KOT (BEYKENT)** – Kot değerleri.
  - **KOLON ISMI (BEYKENT)** – Kolon kesit adı (örn. 60/60).
  - **KIRIS ISMI (BEYKENT)** – Kiriş etiketi.
  - **DONATI YAZISI (BEYKENT)** – Donatı/etriye metinleri.
- **Metin:** Yükseklik 11.5 cm (normal), 9.0 cm (ebad/donati/etriye sol etiketleri); genişlik çarpanı 0.7; stil **YAZI (BEYKENT)** veya **DONATI YAZISI (BEYKENT)**.

(DXF’teki tüm ölçü ve katman detayları `docs/kolon-donati-tablosu-dxf-analiz.md` içindedir.)

---

## 6. Komut Akışı (Özet)

1. Aktif veya seçilen **ST4** dosyasını belirle (veya aynı isimli ST4’ü aç).
2. **Kolon donatı verisi** için sırayla (her zaman önce GPR):
   - Aynı isimli **GPR** dosyasını ara (örn. `SL_09.GPR`); bulunursa “KOLON BETONARME HESAP SONUÇLARI” bölümünü oradan oku.
   - GPR yoksa veya bölüm yoksa aynı isimli **PRN** dosyasını ara (örn. `SL_09.PRN`).
   - **İkisi de yoksa** kullanıcıya veri dosyasının yerini sor (GPR veya PRN yolunu seçtir / gir).
3. ST4’ten **kat listesi** (Story) ve her katın **isim** bilgisini al; tablo **sütun başlıkları** = bu kat isimleri (sırayla).
4. ST4’ten **kolon konumları** (satır sayısı: S1, S2, …) çıkar.
5. PRN veya GPR’deki **KOLON BETONARME HESAP SONUÇLARI** bölümünü parse et; kolon id’ye göre (SB-xx, SZ-xx, S1-xx, …) ebat, donati, etriye metinlerini çıkar.
6. Kat × kolon matrisini doldur (hangi (kat, kolon) için hangi ebat/donati/etriye).
7. Kullanıcıdan **tablo yerleşim noktası** (insert point) al.
8. Tabloyu CM biriminde, belirtilen katman ve ölçülere uygun çiz (LINE + TEXT); font **Bahnschrift Light Condensed**.

---

## 7. PRN / GPR Parse Detayı (Referans)

- **Bölüm:** "KOLON BETONARME HESAP SONUÇLARI" (veya aynı anlama gelen başlık).
- **PRN:** Satırlar doğrudan tablo metni (kolon id, Bx=, By=, Donatı vb.).
- **GPR:** Aynı bölüm bulunur; satırlar bazen **sayısal önek** ile başlar (örn. `130,270,0,0,8,1` + tablo metni). Parser, GPR’de bu öneki atlayıp sadece tablo metnini kullanacak şekilde yazılmalı.
- Her kolon bloğu: İlk satırda kolon id (örn. **SZ-01**, **SB-01**, **S1-02**). Sonraki satırlarda Bx=, By= veya "Polygon", ve **Donatı** sütununda örn. "13Ø14", "ø8/15/8(etriye)", "2Ø4Ø14+2Ø2Ø14(govde)".
- **ebat:** Bx, By değerlerinden (örn. "35", "70", "60/60") veya "Polygon" / poligon kolon kısa adı.
- **donati:** Donatı hücresinde **(etriye)** dışında kalan kısım (örn. "2x4ø16+2x4ø16", "13Ø14").
- **etriye:** Donatı hücresinde **(etriye)** içeren kısım (örn. "ø8/15/8", "ø10/20/20"); parantez içi "(etriye)" isteğe bağlı temizlenebilir.

Bu şartname, KOLONDATA komutu implementasyonu için tek referans olarak kullanılacaktır.

---

## 8. GPR donatı sütunu (karışık ayırıcı ve kodlama)

- Aynı dosyada **UTF-8 `¡` (C2 A1)** ile **tek bayt A1** karışabiliyor; yalnızca string `Split('¡')` yetersiz kalır.
- **Donatı/etriye:** Satır **ham bayıt** olarak bölünür (`C2 A1` veya önceki bayt ≠ C2 iken `A1`). **9. hücre** (+ gerekirse 7–11) UTF-8 / 1252 / 1254 ile çözülüp skorlanır; zayıfsa satırda `(govde)` / `(etriye)` için **regex yedek** devreye girer.
- **Bölüm başlığı:** ASCII **KOLON BETONARME HESAP** ham baytta aranır (Ç bozuk olsa da bölüm bulunur).
- **GPR okuma aralığı (kesin):** Donatı/etriye yalnızca **dosyadaki ilk** `KOLON BETONARME HESAP` satırından **sonra** başlar; **dosyadaki ilk** `Kolon Moment b…` (büyütme katsayısı dipnotu, örn. `ßx,ßy : Kolon Moment büyütme katsayısı`) satırına **kadar** (bu satır dahil değil). Bu aralığın dışı — temel betonarme, bağ kirişi, sonraki bölümler — **okunmaz**. Dipnot bulunamazsa üst sınır: ilk **TEMEL BETONARME** veya dosya sonu.
- **Sayfa sınırı (aynı aralık içinde):** `n,10,h` çerçeve satırı ile sayfalar ayrılır; aralık içinde birden çok **KOLON BETONARME** sayfası birleştirilir.
- Tablo dışı satırların gösterim metni için satır bazlı UTF-8 / 1252 seçimi korunur.
