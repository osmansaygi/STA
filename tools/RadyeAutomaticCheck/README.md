# Radye Temel Planı Otomatik — GPR As ile karşılaştırma

**GÜNCEL: Bu Yardımcı prototipi kullanıcının hedefi değildir ve geri çekildi.** Hedef ST4 ID'deki TEMELDONATI planlarını koruyup sabit üst12/20-alt14/20 üzerindeki ilave As ısı/donatı konturlarını PDF/GPR olmadan çizmektir; bu hedef henüz tamamlanmadı. `ST4_Yardimci_20261020_204827.dll` yanlış otomatik komut kayıtlarını/panel düğmesini kaldırır; metraj/poz/antet düzeltmelerini korur. Standart NETLOAD'a bu DLL kopyalandı. 202713 `rejected-gpr-prototype-202713/` içinde tarihsel araştırmadır. Aşağıdaki eski GPR testi yeni hedefin doğrulaması değildir. Kaldırma doğrulaması: `cleanup-native-result.txt`; teslim: `cleanup-delivery.json`.

Güncel komut `RADYETEMELPLANIOTOMATIK`, kısa adı `RADYEOTOMATIK`. BAPANEL'de ayrı Monolit simgesi vardır. Kullanıcı mevcut GPR + RAD + SO1 yolunu 20.10.2026 tarihinde açıkça kabul etti. Eski 171800 aktarım prototipi geri çekilmiş olarak kalır.

Standart teslim: `NETLOAD/ST4_Yardimci_20261020_202713.dll` ve PDB, Debug/net48. Kaynak derleme `build/gpr-release-check/`; bu klasör adı Release yapılandırması anlamına gelmez. Kullanıcının canlı AutoCAD oturumuna DLL yüklenmedi. Temiz oturumda standart NETLOAD'daki tek Yardımcı DLL yüklenmelidir.

ST4 projesi seçilir, karşılaştırmanın sol-alt yeri cm olarak seçilir. Varsayılan olarak solda STA referansı, sağda yeni otomatik plan; altta X alt/Y üst/Y alt As haritaları yerleştirilir. Referans kapatılabilir. Mevcut RADYETEMEL/RADYEILAVE etkileşimli yolu korunmuştur.

## Dosyalar ve yöntem

Aynı klasörde ST4, RAD, SO1, GPR, MATBQF ve _MatTem.dwg gerekir. PDF/DXF bir girdi değildir. GPR içinde radye ANALİZ SONUÇLARI tablosu her düğümün tasarım momentlerini ve dört As değerini içerir. GPR verileri 0.01 cm²/m hassasiyetinde okunur; geometrinin hassasiyeti RAD'dan korunur. SO1 genel As ve maksimum As kontrolü için kullanılır. Yalnız SO1 kuvvetlerinden noktasal As algoritmasının çözüldüğü iddia edilmez.

MatTem çizimi referans planı ve STA'nın seçtiği çap/aralık/alt-üst/yer bilgisi içindir. MATBQF seçimi doğrular. Otomatik tarafta kaynak REBAR ve ilave yazıları kopyalanmaz. Pozitif GPR ilave As taşıyan elemanlar uygun STA seçimine tekil olarak atanır; çap/aralık alan kapasitesi kontrol edilir. Her grubun destek elemanlarının dış sınır kutusu, mevcut RadyeIlaveRunner bant/trim/gönye/etiket algoritmasına verilir. Bu bir karşılaştırma yöntemidir; STA'nın bar adet/boylarıyla aynı sonuç sözü verilmez. Genel radye ayarları işlem sonunda geri yüklenir ve INI'ye yazılmaz.

Harita, düğüm ilave As=max(kayıtlı As-genel As,0) değerlerinden üretilir. Üçgenlerde doğrusal, dörtgenlerde merkez ortalamasıyla dört alt üçgen kullanılır. Görsel enterpolasyon STA'nın kontur algoritmasıyla bit eşitliği iddiası taşımaz. Sıfır olmayan dönme açısı, 40 cm dışındaki radye kalınlığı ve GPR yuvarlaması nedeniyle eşikte belirsiz düğüm varsa bu sürüm reddeder.

Eksik/farklı proje, eksik/sırasız düğüm, RAD/GPR koordinat farkı, GPR/SO1 tepe As farkı, seçimsiz veya yetersiz ilave alanı reddedilir. Altı kaynak dosyanın SHA256 özeti başta, okuma sonunda ve hedef commit öncesi kontrol edilir. Çizim sırasında dosya değişirse hedef transaction tamamlanmaz. Ön kontrol, hedefe nesne eklenmeden yapılır.

## N_ULK_02 doğrulaması

733 düğüm ve 488 eleman okundu; 516 düğüm plak köşeleridir. STA ekranında kontrol edilen 9 düğümün 18 As değeri GPR ile birebir eşleşti. GPR'nin dört maksimumu SO1 ile 0.01 hassasiyetinde eşit. İlave tepe değerleri X üst0, X alt3.14, Y üst0.53, Y alt1.45 cm²/m.

Altı kaynak dosya içeren ve hiçbir PDF/DXF içermeyen `fixtures/gpr-valid` ile gerçek Core Console testi geçti. 5 alan/5 yeni donatı bandı/5 etiket (alt+üst), 3143 yüzey, üç harita ve mevcut ayarların geri yüklenmesi doğrulandı. Komut kayıtları ve Monolit simgesi geçti. Eksik GPR/koordinat/tepe uyuşmazlığı boş hedefi değiştirmeden reddedildi; dosya değişimi koruması geçti. Orijinal altı dosya test kopyasıyla byte eşit kaldı.

- `gpr-native-release-result.txt`: COMPLETE ve REJECT_COMPLETE.
- `gpr-audit.log`: üretilen DWG yeniden açıldı, 9987 nesne, AUDIT0 hata/0 düzeltme.
- `radye-gpr-otomatik.dwg`: son karşılaştırma çizimi. Aynı isimli DXF test çıktısıdır; kullanıcıdan alınmış bir girdi değildir.
- `radye-gpr-planlar.preview.png` / `radye-gpr-isilar.preview.png`: gerçek test DXF'sinden basitleştirilmiş önizlemeler; tam AutoCAD render'ı değildir.
- `source-final-check.json`: altı kaynak dosyanın byte eşitliği ve SHA256.
- `delivery.json`: standart net48 DLL özeti; Plan ve ortak bağımlılıkları `netload-unchanged-manifest.json` ile sabittir.

İzole net48 derleme0 hata/0 uyarı. `tools/Verify-Project.ps1` dört PASS; `tools/RadyeHeatmapInspect/test_gpr_heatmap.py` yedi test PASS. Native koşucu net10/AutoCAD2027'dir; bu doğrulama net48 dağıtımın AutoCAD2027 desteği beyanı değildir. Canlı kullanıcı oturumundaki modal form/panel görsel testi yapılmadı.

Önceki 161449 DLL/PDB `onceki-stabil-161449` altında saklıdır. Standart NETLOAD'da yalnız 202713 Yardımcı sürümü vardır. 171800 prototipi `geri-cekilen-prototip` altında tarihsel kayıt olarak tutulur.
