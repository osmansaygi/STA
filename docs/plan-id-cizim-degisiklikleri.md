# ST4 Plan ID Çizim – Konuşma Özeti ve Yapılan Değişiklikler

Bu belge, ST4_Plan_ID_Ciz projesi üzerinde yapılan istekler ve uygulanan değişiklikleri özetler.

---

## 1. Temel plan – Perde ID ve etiket

- **İstek:** Temel planındaki perde **ID’leri** kapatılsın; **etiketler** kalsın.
- **Yapılan:** `DrawWallsForFloor` içinde perde merkezine yazılan ID metni (`MakeCenteredText(..., beam.BeamId, ...)`) kaldırıldı. Temel planında sadece perde poligonları ve kolon diff çiziliyor; perde etiketleri (`DrawPerdeLabelsForFloor`) duruyor.

---

## 2. Kat sınırı – Vertex temizliği

- **İstek:**  
  - İki vertex arasında **1 mm’den az** mesafe varsa vertex kaldırılsın.  
  - Aynı doğrultuda devam eden segmentler arasındaki gereksiz vertex’ler (collinear) silinsin.
- **Yapılan:**  
  - `DrawGeometryRingsAsPolylines` metoduna `minVertexDistCm` ve `collinearTolCm` parametreleri eklendi.  
  - Collinear pass: B vertex’i A–C doğru parçasına `collinearTolCm` mesafede ise B kaldırılıyor (`PointToSegmentDistance`).  
  - Kat sınırı çağrısı: `minVertexDistCm: 0.1` (1 mm), `collinearTolCm: 0.05`.

---

## 3. Aks sınırı katmanı ve poligon

- **İstek:**  
  - “AKS SINIRI (BEYKENT)” katmanı tanımlansın.  
  - Eğimsiz kolon akslarının en sol, en sağ, en alt, en üst kesişim noktalarıyla 4 kenarlı poligon çizilsin.  
  - Çizilen aks sınırı kat sınırı içinde kalıyorsa kat sınırına kadar “stretch” edilsin.
- **Yapılan:**  
  - `LayerAksSiniri = "AKS SINIRI (BEYKENT)"` eklendi; `EnsurePlanLayer` ile oluşturuluyor.  
  - `DrawAxisBoundary`: 4 köşe noktasıyla dörtgen; kat sınırı envelope’ına göre üst/alt/sol/sağ kenarlar hizalanıyor (stretch).  
  - Sonra **aks sınırı çizimi kapatıldı:** `DrawAxisBoundary` çağrısı kaldırıldı (çizimlere eklenmiyor). Aks çizgisi mesafesi referansı olarak aks sınırı zarfı (`GetAksSiniriEnvelope`) kullanılmaya devam ediyor.

---

## 4. Aks çizgisi mesafesi

- **İstek:** Aks çizgileri **200 cm** uzakta bitsin; referans **aks sınırı** olsun.
- **Yapılan:**  
  - `AxisExtensionBeyondBoundaryCm = 200.0` (önceden 220/240).  
  - `GetAksSiniriEnvelope(elementUnion)` eklendi: eğimsiz kolon akslarından 4 köşe, kat sınırına stretch, model koordinatında zarf.  
  - `DrawAxes` artık `GetAksSiniriEnvelope` ile verilen zarfı kullanıyor (kat sınırı değil).

---

## 5. Birleşik katman

- **İstek:** Birleşik katman artık çizilmesin.
- **Yapılan:** `DrawUnifiedLayer` içinde `LayerBirlesikKatman` ile yapılan `DrawGeometryRingsAsPolylines` satırı kaldırıldı. Sadece kat sınırı ve `_lastKatSiniriGeometry` güncellemesi kaldı.

---

## 6. Kolon etiketleri (genel)

- **İstek:** Kolonlara resimdeki gibi etiket: 1. satır isim (SB-43), 2. satır boyut (40/40). Poligon kolonlarda sadece isim.
- **Yapılan:**  
  - `AppendColumnLabel` eklendi.  
  - Dikdörtgen/dairesel: iki satır (SB-{no}, (W/H)).  
  - Poligon: tek satır (SB-{no}).

---

## 7. Kolon etiketi katmanı ve stil

- **İstek:**  
  - “KOLON ISMI (BEYKENT)” katmanı: renk 91, kalınlık 0,2.  
  - Etiket: justify **Left**, yazı stili **ETIKET**, yükseklik **12**.  
  - Konum: kolon sağ alt köşesinden 22 cm aşağı, 10 cm sağ; isim–boyut arası **18 cm**.
- **Yapılan:**  
  - `LayerKolonIsmi`, renk 91, `LineWeight020`.  
  - `GetOrCreateElemanEtiketTextStyle` (ETIKET) kullanılıyor; yükseklik 12.  
  - Sol hizalı, referans noktası: açısızda sağ alt köşe, açılıda en alt nokta; isim 22 cm aşağı 10 cm sağ, boyut ismin 18 cm altında.

---

## 8. Konum ve mesafe ayarları

- **İstek:** Renk 91; konum 22 cm aşağı, 10 cm sağ; isim–boyut arası 18 cm.
- **Yapılan:** Sabitler güncellendi (`offsetRightCm = 10`, `offsetNameDownCm = 22`, `gapNameToDimCm = 18`).

---

## 9. Justify ve eğimli kolon etiketleri

- **İstek:**  
  - Justify sadece **Left** (bottom left / top left değil).  
  - Eğimli kolonlarda 1. resim gibi (eğik yazı) değil, 2. resim gibi (yatay yazı) olsun.
- **Yapılan:**  
  - `TextHorizontalMode.TextLeft`, `TextVerticalMode.TextBottom` (sadece Left + baseline).  
  - Eğimli kolonlarda etiket **döndürülmüyor** (Rotation = 0); konum dünya koordinatında ref + (10, -22) ve ref + (10, -40).  
  - `GetColumnLabelReferencePoint`: açısızda sağ alt köşe, açılıda en alt nokta (min Y).

---

## DLL ve kurallar

- **ST4_Plan_ID_Ciz:** Her derlemede tarih/saatli DLL: `ST4_Plan_ID_Ciz_YYYYMMDD_HHmmss.dll`.  
- **Klasör:** `ST4_Plan_ID_Ciz\bin\Debug\net48\` (veya Release).  
- **AutoCAD:** NETLOAD → bu klasör → en güncel `ST4_Plan_ID_Ciz_*.dll`.  
- **ST4_Aks_Ciz_CSharp:** Sabit isim `ST4_Aks_Ciz_CSharp.dll`; referans için aynı klasörde bulunur.

---

*Belge, konuşma özetine göre oluşturulmuştur.*
