# Birleştirme Kuralları (Temel Hatılı ve Diğer Elemanlar)

Bu belge, temel hatıllarında (bağ kirişleri) uyguladığımız birleştirme mantığını özetler. Aynı pattern diğer elemanlar (sürekli temel parçaları, radye bölgeleri, vb.) için de kullanılabilir.

---

## 1. Genel Prensip

**Bitişik/temas eden aynı tür elemanlar tek tek çizilmemeli; önce Union ile tek geometriye birleştirilip sonra çizilmelidir.**

- Ayrı ayrı çizince: BK18 ve BK47 gibi bitişik iki hatıl iki ayrı polyline olur, köşede çakışma/boşluk görünebilir.
- Union sonrası çizince: Tek bir birleşik poligon (L/T şekli) çizilir, köşeler düzgün birleşir.

---

## 2. Temel Hatılı İçin Uygulanan Akış

Referans: `PlanIdDrawingManager.DrawTieBeams`

### Adımlar

1. **Poligonları topla**  
   Aynı katman/aynı tür elemanlar için (örn. sadece TEMEL HATILI bağ kirişleri) her elemanın poligonunu hesapla ve bir `List<Geometry>` içine ekle.

2. **Union uygula**  
   Toplanan poligonları NetTopologySuite ile birleştir:
   - Tek poligon varsa: doğrudan kullan.
   - Birden fazla varsa: `CascadedPolygonUnion.Union(hatiliPolygons)` ile tek geometri yap.

3. **Kolon/perde kesimi (isteğe bağlı)**  
   Birleşik geometriden kolon+perde alanını çıkar:
   - `unionHatili.Difference(kolonPerdeUnion)`  
   Böylece kolon/perde içinde kalan hatıl parçaları çizilmez.

4. **Çizim**  
   Son geometriyi **tek seferde** polyline halkalarına dönüştürüp çiz:
   - `DrawGeometryRingsAsPolylines(tr, btr, toDraw, layerHatili)`  
   Bu fonksiyon saç teli temizleme (paralel gap 2 mm, kısa segment 4 mm) kurallarını uygular.

5. **ID / etiket yazıları**  
   Birleştirme öncesi, her elemanın merkezini hesaplayıp yazıyı o eleman bazında ekleyebilirsin (BK18, BK47 vb. ayrı ayrı kalır).

### Özet sıra

```
Elemanları döngüyle işle
  → Her biri için poligon hesapla
  → İstenen türdeyse (örn. TEMEL HATILI) listeye ekle
  → İstersen merkez noktada ID yazısı çiz
Tüm listeyi Union ile birleştir
  → Difference(kolonPerdeUnion) uygula (gerekirse)
  → DrawGeometryRingsAsPolylines ile tek seferde çiz
```

---

## 3. Diğer Elemanlara Uygulama

Aynı mantık şunlar için kullanılabilir:

| Eleman türü              | Toplama kriteri        | Union sonrası        | Not |
|---------------------------|------------------------|------------------------|-----|
| Temel hatılları (bağ kirişi) | Aynı katman (TEMEL HATILI) | Difference(kolonPerde) → çizim | Uygulandı. |
| Sürekli temel şeritleri   | Aynı katman / aynı tip | Difference(kolonPerde) → çizim | Zaten birleşik çizimde kullanılıyor. |
| Radye plak parçaları      | Aynı katman            | İstenirse Union → çizim | Slab foundations benzeri. |
| Başka hat/şerit elemanlar | Kendi gruplama kuralın | Difference gerekirse uygula → çizim | Hep `DrawGeometryRingsAsPolylines` kullan. |

Kural: **Aynı katmanda, bitişik veya kesişen aynı tür çizilecek poligonlar varsa önce topla → Union → (Difference) → tek çizim.**

---

## 4. Zorunlu Çizim Kuralları (.cursor/rules ile uyumlu)

- **Union/Difference sonrası çizim** mutlaka `DrawGeometryRingsAsPolylines` (veya aynı mantıktaki bir yardımcı) üzerinden yapılmalı; böylece:
  - Paralel (±1°) iki kenar arasında dik mesafe **&lt; 2 mm** ise köşe birleştirilir (saç teli temizlenir).
  - **4 mm’den kısa** segmentler elenir.
- Kolon/perde kesiminde **Buffer ile boşluk açma**; ince parça temizliği sadece yukarıdaki paralel-gap ve kısa-segment filtreleriyle yapılır.

Detay için: `.cursor/rules/temel-birlesim-ve-hatil.mdc`

---

## 5. Temel üçgen artık temizleme kuralı

Temel (sürekli temel, radye, bağ kirişi vb.) birleşik çiziminde **ApplyRingCleanup** ve **DrawGeometryRingsAsPolylines** içinde `applySmallTriangleTrim: true` kullanılır.

**Kural (1d – düz hattaki ufak üçgen):**

- Kapalı halkada ardışık 5 nokta: **X – A – B – C – Y**.
- **A, B, C** bir üçgen oluşturuyor; alanı **1 cm² ile 1000 cm²** arasında.
- **X–A** segmenti ile **C–Y** segmenti aynı doğrultuda (paralel, ≈1° tolerans).
- **C–Y** doğrusu, **X–A** doğrusunun üzerinde sayılıyor (SegmentCYOnLineXA).

Bu koşullar sağlanıyorsa **A, B, C** vertex’leri halkadan silinir; **X** ile **Y** doğrudan birleştirilir. Böylece kesim/union sonrası oluşan düz hattaki küçük üçgen çıkıntılar temizlenir.

**Not:** Bu kural halka üzerinde ardışık vertex’lere uygulanır (ring cleanup). Ayrı bir poligon olan 3 köşeli küçük parçalar için **FilterSmallTriangleRemnants** (örn. kat sınırında 500 cm² eşiği) kullanılır.

Detay kod: `ApplyRingCleanup` (minTriangleAreaCm2 = 1.0, maxTriangleAreaCm2 = 1000.0), `DrawGeometryRingsAsPolylines(..., applySmallTriangleTrim: true)`.

---

## 5. Kod Referansı

- **Bağ kirişi birleştirme:** `ST4_Plan_ID_Ciz/PlanIdDrawingManager.cs` → `DrawTieBeams`  
  - `hatiliPolygons` toplanıyor → `CascadedPolygonUnion.Union(hatiliPolygons)` → `Difference(kolonPerdeUnion)` → `DrawGeometryRingsAsPolylines`.
- **Genel temel birleşim:** Aynı dosyada `BuildTemelUnion`, `DrawTemelMerged`, `DrawGeometryRingsAsPolylines`.

Bu kurallar başka eleman türlerine eklenirken aynı pattern (topla → Union → [Difference] → DrawGeometryRingsAsPolylines) takip edilmelidir.
