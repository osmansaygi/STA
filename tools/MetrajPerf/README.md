# DONATIMETRAJ performans doğrulaması

`Build.ps1 -OutputName after`: mevcut restore önbelleğiyle izole Debug/net48 derleme. Ortak NETLOAD/bundle/setup hedeflerini kapatır; ortak bin/obj kullanmaz.

`Native/Native.csproj`: yalnız test koşucusu; AutoCAD 2027/net10 API'si. `run.scr` ile ayrı Core Console sürecinde geçici veritabanlarını kullanır, kullanıcı çizimini kaydetmez. Ürün DLL'sinin hedefi net48 olarak kalır.

`VerifyNative.ps1`: kaydedilmiş önceki DLL ile yeni DLL'yi 1/31/32/33/1000 pozda, renkli/renksiz karşılaştırır. Tam çıktı, XData bağlantıları ve çizim sırası dosyalarının hash'leri ile kg değerleri eşit olmalıdır. `-CompareOnly -LargeRetryResult tools/MetrajPerf/case-1000-after-retry.txt` mevcut tamamlanan kayıtları karşılaştırır.

`final-validation.txt` başarılı karşılaştırmadır. `native-validation.txt` ilk çalışmadaki tekil Core Console sonlanmasını da saklar; aynı DLL'nin aşama kayıtlı tekrarı `case-1000-after-retry.txt` içinde COMPLETE olmuştur. Bu sonlanma çözüldü iddiası yoktur. Büyük eski DWG açılışındaki ayrı sonlanma nedeniyle tam kullanıcı çizimi uçtan uca ölçümü yapılmadı.

Ölçüm: 20.000 ek çizgi + 1000 poz. Ortak tablo hazırlama ~2,9 s→0,2 s, doğrudan renkli balon üretimi ~6,5 s→0,058 s. Antet hızlı balon yolu değiştirilmedi. Bunlar toplam komut süresi değil, ölçülen bileşenlerdir.

Toplu sıralama API'si mevcut sıralama yuvalarında verilen sırayı uygular: [Autodesk SetRelativeDrawOrder](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_DrawOrderTable_SetRelativeDrawOrder_ObjectIdCollection.html). Eski tarama→kontur→rakam sırası ve diğer nesnelerin sırası testte aynıdır.
