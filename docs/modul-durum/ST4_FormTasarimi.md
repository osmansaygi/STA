# Yardımcı ve Plan — Standart A açık formlar

Güncelleme: 22.10.2026. Kullanıcı, gösterilen A / Standart düzenini onayladı ve bütün kot kutularında cm istedi. Önceki Monolit/koyu form tercihi geçersizdir.

## Güncel teslim

- `NETLOAD/ST4_Yardimci_20261022_150054.dll` ve `NETLOAD/ST4_Plan_ID_Ciz_20261022_150054.dll` + PDB. İkisi de Debug/net48; izole derlemeler 0 hata/0 uyarı. net10/AutoCAD 2027 desteği iddiası yoktur.
- Kaynak-kopya DLL/PDB SHA256 eşit. Kanıt: `tools/MonolitFormCheck/standard-delivery.json`.
- Önceki Yardımcı 013049 ve Plan 142128 sürümleri `tools/MonolitFormCheck/onceki-netload-20261022_150054/` içinde yedeklendi. Önceki Yardımcı dosyasını silme erişime kapalı olduğundan silme zorlanmadı; eski dört DLL/PDB NETLOAD içinde de durur. `NETLOAD/OKU.txt` yeni 150054 çiftini gösterir. Diğer 15 dosyanın hash'i aynı.
- Ana bin/obj, ofis setup, bundle ve başka ajanın kaynakları değiştirilmedi. Güncel Plan kaynakları, önceki 142128 tesliminin ana donatı yazısı dahil derlemeye alındı.

## Görünüm ve kot birimleri

- 25 kaynak dosyasında 26 form sınıfı `StandardFormTheme` ile daima açık. Açık gri zemin, beyaz girişler, yerel Windows düğme/kutu çizimi, normal radyo/onay kutuları, standart GroupBox çerçeveleri ve sağa hizalı alt düğmeler. Formlara COLORTHEME olayı aboneliği gerekmez; AutoCAD koyu olsa da formlar açık kalır. Panel renk adaptörleri ve Monolit simgeler değişmedi.
- Genel ayarlar alt alta görünür; kısa ekranda içerik kaydırılır, alt işlem satırı görünür kalır. Katman/stil editörünün mevcut üç tablo sekmesi korunur. Mevcut kontrol nesneleri ve olayları yeniden kullanılır; hesap/çizim kaynakları değiştirilmedi.
- Oto Donatı ve Sehpa Donatı üst kot girişleri metre yerine cm. Örnek: -0,80 m ayarı -80 cm açılır. Eski INI/profile `UstKotM` alanları metre olarak korunur: gösterim x100, normal/sessiz kayıt /100. Sayısal aralıklar aynı fiziksel sınırları korur; kesirli cm desteklenir. Profil değişimleri dönüştürülür.
- Kuranglez, Merdiven, Parapet, Kot Değiştir, Temel Çukuru, Temel Detayı ve Temel Hatılı kot girişleri zaten cm; aynı kaldı. Kiriş Açılım kat seçimi kot gösterimi de cm oldu. Çizim kot yazılarının alışılmış metre gösterimi değiştirilmedi.

## Doğrulama

- `tools/MonolitFormCheck/standard-test-result.txt`: 26 gerçek form sınıfı PASS. Her iki host temasında açık kalma, native düğmeler, kontrol eşlemesi (eşlenmemiş 0), yatay sınırlar, Tek/Çift/AKS, kat seçimi, kiriş/sakal, katman araması ve olay aboneliği temizliği doğrulandı.
- Kot testleri: eski metre kaydını cm açma, normal ve sessiz kayıt, negatif/pozitif kesirli cm, Radye/Mantar profil geçişi ve geri dönüş PASS. Test kopyalarında yalnız AutoCAD ve disk erişimi taklit; kullanıcı ayarlarına/çizimlerine yazılmaz.
- Gerçek WinForms test görüntüleri `tools/MonolitFormCheck/preview/` altında. Oto Ölçü, Döşeme Donatı, Kuranglez, Oto Donatı ve Sehpa görsel kontrol edildi. Görseller imagegen taslağı değildir.
- `pwsh -NoProfile -File tools/Verify-Project.ps1`: YardımcıStartup ve StaStartup PASS; görev öncesinden kalan BaDimensionStyles testindeki eksik `BeykentOrtak` başvurusu nedeniyle CS0246 ile durur. Genel doğrulama tamamen başarılı değildir.
- Kalan TemelDetayGeometry ve TemelIlaveKutuBol testleri ayrı çalıştırıldı, PASS.

## Açık doğrulama

Canlı AutoCAD'e otomatik DLL yüklenmedi. Temiz oturumda 150054 tarihli iki DLL ile form açma, çizimden seçim dönüşü ve yüksek DPI kullanıcı ortamında doğrulanmalı. Derleme, kopyalama ve taklit host testleri canlı AutoCAD doğrulaması sayılmaz.
