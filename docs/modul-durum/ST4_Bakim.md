# 23.10.2026 — genel temizlik ve çizim dışı stabilite

Kullanıcı, Osman_donusum klasörünün temizlenmesini ve çizim kodları korunarak genel stabilite incelemesini istedi.

## Temizlik

`tools/ProjectMaintenance20261023/cleanup-plan.json` listesindeki 7.733 izlenmeyen eski çıktı/günlük, kaynakları kaldırılmadan önce ZIP'e alındı. ZIP içindeki her dosya SHA-256 ile tekrar okundu ve doğrulandı; orijinalin hâlâ aynı olduğu kontrol edilerek yalnız listedeki dosya kaldırıldı. Atlanan/kayıp dosya yok; 4 boş arşiv klasörü kaldırıldı. Sembolik/reparse dizinleri takip edilmedi; toplu recursive klasör silme yapılmadı.

- 5,196 GiB eski dosya → 1.109,5 MiB doğrulanmış ZIP; mantıksal azalma yaklaşık 4,11 GiB. Hard link günlükleri nedeniyle bu sayı fiziksel disk boşluğu ölçümü değildir.
- 4.258 eski kök AutoCAD günlüğü; 772 eski türetilmiş RunAll DWG/DXF çıktısı; 2.542 bağlı tanılama; 119 eski sürüm yedeği; 42 eski ortak paket dosyası.
- RunAll çıktısı için COMPLETE kanıtı arandı; her senaryonun son iki DWG/DXF'si, son 20 dakikalık dosyalar, kaynak kodun açıkça referansladığı çıktılar ve son iki ortak rollback paketi korundu.
- Kaynaklar, gerçek DWG/DXF/ST4/GPR/PDF girdileri, test araçları/fixture'ları, lisans/setup/bundle, .git ve güncel NETLOAD/GUNCEL_2027 korunur.
- Arşiv: `tools/ProjectMaintenance20261023/arsiv/temizlik-20261023-195413.zip`. İçindeki `_cleanup-manifest.json`, özgün proje göreli yolları ve dosya SHA-256 değerlerini içerir. Gerekirse ayrı bir klasöre açıp istenen dosyayı eski yoluna geri kopyalayın; güncel dosyaların üzerine körlemesine açmayın.
- Tekrar bakım için önce `inventory.py`, ardından `cleanup.py` yalnız plan üretir; `cleanup.py --apply` arşiv/doğrulama sonrasında listedeki değişmemiş dosyaları kaldırır. Her yeni plan için aktif oturum/koordinasyon kontrolü gerekir.

## Stabilite ve doğrulama

Üretimde yalnız Yardımcı `CommandEntry.ResolvePluginDependency` değişti. Aynı adlı fakat yanlış sürümlü/anahtarlı/kültürlü yerel DLL'yi körlemesine yükleyen davranış, üretim metodunu çalıştıran regresyonda düzeltme öncesi üretildi (`dependency-before.txt`). Artık yüklemeden önce AssemblyName metadata'sı istekle karşılaştırılır; uyuşmayan dosyada host çözümleyicisine bırakılır. Doğru DLL'nin yüklenmesi korunur. Yerel arama yalnız eklenti klasöründedir; bozuk dosya/eksik bağımlılık/satellite durumları güvenli reddedilir. Bu dar değişiklik yerel yanlış DLL bağlanma riskini azaltır; tüm native AutoCAD çökmelerinin kök nedeni olarak sunulmaz.

Genel gözden geçirmede açılış/kapanış, Idle/ribbon abonelikleri, palet/form yaşam döngüsü, simge sahipliği ve ortak stil girişleri okundu; iki modül SDK analizörleriyle derlendi. Çizim/hesap yöntemlerinde, geometride, katman/stil üretiminde refaktör yapılmadı. CommandEntry dışındaki 168 üretim kaynağı/proje dosyasının işlem öncesi/sonrası SHA-256 değerleri aynı (`protected-source-hashes.json`).

Genel test altyapısının eski `BeykentOrtak` CS0246 hatası giderildi: stil regresyonu gerçek ortak ayar kaynağını ve dört gerçek stil metodunu derler; kullanıcının ayar dosyasına dokunmadan üretim varsayılanlarını bellekte kullanır. Yeni bağımlılık regresyonu Verify-Project'e eklendi. `.gitignore` yeniden üretilebilir derleme klasörlerini ve bakım/dağıtım arşivlerini dışarıda tutar.

- `pwsh -NoProfile -File tools/Verify-Project.ps1`: bütün kontroller PASS.
- `Test-YardimciStartup.ps1 -HomeOnly`: eski palet 0, Home başlatma 1; PASS.
- `Test-HomeRibbon.ps1`: mevcut dört grup/20 komut, 16px görseller, yeniden kurulum ve kapanış abonelikleri PASS.
- Yardımcı Debug/net10.0-windows + SDK analizörleri: 0 hata/0 uyarı.
- Plan Debug/net10.0-windows + SDK analizörleri, izole salt doğrulama derlemesi: 0 hata/4 mevcut uyarı. İki NU1510 CodePages paket ayıklama uyarısı, iki SYSLIB0014 ServicePointManager/WebRequest uyarısı (CsbBirimFiyatCatalog). Paket/ağ API dönüşümü ve çizim değişikliği yapılmadı.
- Ayrı Core Console 2027: yeni Yardımcı ve değişmeyen Plan birlikte NETLOAD; modül/NTS yolları PASS. Üç eski palet aliası UI hostunu yüklemez; antetli CK sürekli temel ve aynı çizimde tekrar COMPLETE, exit=0. Kaynak DWG/ST4 kaydedilmedi. Test çalışma dizini proje kökü yerine bakım klasörü seçildi.

## Güncel teslim ve kısıt

Tek adres `NETLOAD/GUNCEL_2027/`: `ST4_Yardimci_2027_20261023_195648.dll` + değişmeyen `ST4_Plan_ID_Ciz_2027_20261023_190204.dll`. Publisher kopya hash kontrolü geçti, Plan hash aynı. Masaüstü kısayolu aynı klasöre gider; eski paket rollback arşivinde. AutoCAD tamamen kapatılıp yeni Yardımcı tek kez NETLOAD edilmelidir. Çalışan kullanıcı AutoCAD oturumuna DLL yüklenmedi.

Canlı AutoCAD arayüzü/bütün komutlar bu bakımda denenmedi. Önceki bağımsız .NET/csc/mscorsvw erişim ihlallerinin sistemsel kök nedeni açık kalır. Dosya temizliği veya analizör geçişi çökmezlik kanıtı değildir. net48/ofis/setup bu teslimde derlenmedi veya dağıtılmadı.

Kanıtlar `tools/ProjectMaintenance20261023/`: inventory.json, cleanup-plan.json, cleanup-result.json, protected-source-hashes.json, dependency-before/after.txt, verify-final.txt, ribbon-test.txt, build-analysis.txt, plan-build-analysis.txt, legacy-palette.txt, package-probe.txt, core-repeat.txt, core.console.log, publish.txt.
