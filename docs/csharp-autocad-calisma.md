# C# / AutoCAD çalışma düzeni

## Codex ayarları

Bu projenin `.codex/config.toml` dosyası GPT-6.1 Sol ve high düşünme seviyesini varsayılan seçer. Kullanıcının mevcut sohbet seçimi veya yönetilen ayarlar öncelikli olabilir; bu dosya açık sohbetin modelini anında değiştirmez. High, çok dosyalı C# ve hata araştırmasına yöneliktir; model menüsünden basit işler için düşürülebilir. Yüksek düşünme seviyesi daha fazla süre/kota kullanabilir.

Türkçe, kısa sonuç raporu ver. Kullanıcı istediğinde yetki kapsamında işi tamamla; rutin kararları kendin al. Kalıcı çizim ve Monolit simge tercihlerini koru. Cursor ve Codex ortak klasörde `docs/ajan-koordinasyon/PROTOKOL.md` ile çalışır.

## Kod değişikliği

- Önce ilgili modül durumunu, teknik kuralları ve güncel dosyaları oku. Eski sohbetleri ancak proje hafızası yetersizse araştır. Hata için ilgili günlük ve küçük örnekten başla; büyük DWG/log klasörlerini körlemesine tarama.
- Geçerli çizim, donatı hesabı, ölçü ve kullanıcı akışını koru. Kaynak yönetimi, hatalı girdi ve kanıtlanmış darboğazlarda küçük düzeltme yap. Toplu format, yeniden adlandırma, paket güncellemesi veya geniş refaktörü görev dışına taşıma.
- AutoCAD nesnelerine arka plan thread'inden erişme. Modeless/oturum işlemlerinde uygun belge kilidi ve komut bağlamını kullan; işlem öncesi belgenin hâlâ geçerli olduğunu doğrula.
- `new`, `Clone`, offset, explode ve geometri API dönüşlerinde sahipliği takip et. Transaction'a kaydedilmemiş native nesneleri `using/finally` ile temizle. Transaction'ın açtığı nesnelerin ömrünü gereksiz veya çift Dispose ile bozma; transaction dışına canlı DBObject taşıma.
- Event/timer aboneliklerini yaşam döngüsünde yönet; AutoCAD kapanırken yok edilmiş native pencereye erişme. Genel `catch` ekleyerek native göçmeyi çözdüğünü iddia etme.
- `.cursor/rules/autocad-nts-stabilite.mdc` dosyasını geometri işlerinde oku. Büyük geometride gereksiz overlay/union/tekrarlı tarama yapma; performans iyileştirmesinde önce somut darboğaz ve aynı sonuç kontrolü ara.
- DLL'yi çalışan AutoCAD'e otomatik yükleme. Aynı oturumda tarihli eski/yeni kopyalarla doğrulama yapma. Çıktının hedef sürümünü açık yaz.

## Sürüm ve doğrulama

- Ana projelerin mevcut varsayılanı `net48`; Yardımcı ayrıca `/p:AutoCAD2027Build=true` ile `net10.0-windows` hedefler. Plan ID de 23.10.2026'dan beri `/p:AutoCAD2027Build=true` ile `net10.0-windows` (AutoCAD 2027 API, çıktı `ST4_Plan_ID_Ciz_2027_*`, NETLOAD kökü otomatik kopyası kapalı) derlenir; kod değişmeden derlendi, Core Console 2027 sürekli temel testleri geçti. Tam komut kapsamı canlı 2027'de doğrulanmadı. net48 Plan ID 2027'de yüklenirse Transaction.Commit sırasında yığın bozulmasıyla göçer; 2027 kullanıcısına `NETLOAD\AutoCAD2027\` altındaki derleme verilir.
- Varsayılan çözüm yalnız Plan ID içerir; iki modülün derlendiğini söylemeden önce ikisini ayrı doğrula.
- 23.10.2026 dağıtım düzeltmesi: Plan ID net48 çıktısı yalnız `NETLOAD/Legacy-net48/` altına kopyalanır. Kök NETLOAD'daki net48 DLL, AutoCAD 2027'ye yanlış yüklenip yeniden çökme üretti (16:35 dökümü). 2027 için `NETLOAD/AutoCAD2027/` içindeki `_2027_` DLL kullanılır; net48 köke geri konmaz. Yardımcı dağıtımı bu değişiklikten bağımsızdır.
- **Kullanıcının güncel teslim tercihi (23.10.2026 17:35):** AutoCAD 2027 Yardımcı ve Plan için tek seçim adresi `NETLOAD/GUNCEL_2027/`. Masaüstü ve proje kökündeki `GUNCEL DLL - AutoCAD 2027.lnk` bu sabit klasörü açar. Yeni teslimde `tools/Publish-CurrentNetload2027.ps1 -YardimciDll <güncel-net10-dll> -PlanDll <güncel-net10-dll>` çalıştır; değişmeyen modül için bu klasördeki mevcut DLL kullanılabilir. İki modülün de net10/2027 olduğu metadata ile doğrulanır, ortak bağımlılıklar hash ile karşılaştırılır; eski paket `NETLOAD/arsiv/` içinde korunur. Paketleme öncesi koordinasyon kaydında bu klasörü ayır, kullanıcı AutoCAD oturumuna DLL yükleme. net48 veya tarihli arşiv klasörlerini güncel yükleme adresi olarak verme. Ham derleme/tarihli sürüm klasörleri geliştirici kaynağıdır; güncel kullanıcı teslimi yayınlama betiğiyle ayrıca yenilenir.
- Testleri tek komutla çalıştır: `pwsh -NoProfile -File tools/Verify-Project.ps1`.
- Legacy iki modül derlemesi ve testler: `pwsh -NoProfile -File tools/Verify-Project.ps1 -BuildTarget Legacy`.
- Yalnız Yardımcı 2027 derlemesi ve testler: `pwsh -NoProfile -File tools/Verify-Project.ps1 -BuildTarget Yardimci2027`. Bu, Plan ID 2027 desteği sağlamaz.
- SDK'nin yerleşik .NET analizörleriyle ek uyarı taraması: yukarıdaki açık derleme hedefine `-Analyze` ekle. İlk geçişte uyarıları değerlendir; yanlış pozitifler nedeniyle tüm kodu otomatik dönüştürme. Analizörler native AutoCAD göçmelerini garantiyle bulmaz.
- Derlemeler mevcut restore önbelleğini kullanır. Restore eksikse ihtiyaç duyulan projeyi ayrıca restore et; bağlantı hatasını başarı sayma. Derleme hedefleri NETLOAD ve Yardımcı dağıtım çıktılarını günceller; önce koordinasyon kaydıyla kaynakları ayır.
- Genel değişiklikte mevcut regresyonları çalıştır; küçük değişiklikte ilgili testi seç. Yeni test, hata davranışını yakalamalı. Testler host taklitleriyle çalışır; AutoCAD içinde çalışma kanıtı değildir.
- Teslimde test sonucu, hedef framework, tam DLL yolu ve AutoCAD içinde denenen/denenmeyen işlemleri ayır. Doğrulanmayan hız artışı veya çökmezlik sözü verme.
