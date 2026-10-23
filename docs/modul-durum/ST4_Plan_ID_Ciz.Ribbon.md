# Plan — BEYKENT sekmesi

Güncelleme: 23.10.2026, Codex / 4 numaralı görsel önerinin uygulaması.

- AutoCAD 2027 / net10.0-windows: ayrı **BEYKENT** sekmesi, altı grup ve 19 mevcut komut. Temel (5), Kolon (4), Kiriş (2), Yapı (4), Detay / İskele (3), Metraj (1). 32px Monolit simgeleri ve iki satırlı etiketler.
- Mevcut Plan simge sağlayıcısı kullanılır: kişisel simge > Monolit > klasik. Atlas/eşlemeler değiştirilmedi. Simge tercihi değişince yalnız bu sekme komut bağlamında yenilenir.
- Açılışta sekme eklenir; kullanıcının aktif sekmesi korunur. `BEYKENT` sekmeyi açar/seçer; `BEYKENTKAPAT` gizler. CUI veya çalışma alanı dosyası yazılmaz. Workspace/ribbon yeniden açılışında çoğalmadan yeniden kurulur.
- 2027'de eski STA paleti otomatik açılmaz; `STAPANEL` elle çalışır. Sekmede olmayan ST4 Plan ID, Kolon Data, ST4 Kesit, Katman temizle ve deneme komutları eski palet/komut satırından kullanılabilir. net48 başlangıcı değişmedi; Ribbon kodu yalnız AUTOCAD2027 ile derlenir.
- Çizim/hesap yöntemleri değiştirilmedi. CommandEntry yalnız Initialize/Terminate; csproj yalnız 2027 sabiti ve kopyalanmayan AdWindows referansı. Üç yeni BeykentRibbon*.cs sunum/komut köprüsüdür.

## Doğrulama

- `tools/PlanRibbonCheck/Test.ps1 -Render`: gerçek AdWindows WPF sınıfları ve üretim simge sağlayıcısıyla 19 gerçek komut kaydı, altı grup sırası, 32px frozen simgeler, doğru komut çağrıları, çoğalmama, Home/Yardımcı/aynı başlıklı başka sekme koruma, workspace yenilenmesi, simge yenilemesi, gizleme ve bekleyen otomatik kurulum, abonelik kapanışı PASS. `actual-ribbon.png` ayrı WPF renderıdır; canlı AutoCAD ekran görüntüsü değildir, tema AutoCAD'de hosttan gelir.
- `Test-StaStartup.ps1 -Beykent2027`: yalnız Ribbon bir kez başlar, eski STA paleti kuyruklanmaz, ikinci assembly başlangıcı engellenir. Varsayılan legacy fixture PASS.
- `tools/Verify-Project.ps1`: tüm genel regresyonlar PASS.
- `tools/PlanRibbonCheck/Build.ps1`: gerçek üretim kaynakları, izole build/obj; net10.0-windows 0 hata / 4 mevcut uyarı (NU1510 iki kez, SYSLIB0014 iki kez). Ortak bin/obj ve Yardımcı derlemesi kullanılmadı.
- `tools/PlanRibbonCheck/Run-Native.ps1`: ayrı Core Console 2027 sürecinde iki modülün/NTS'nin doğru klasörden yüklenmesi, üç Ribbon komutunun kaydı ve başlangıçta native palet hostunun yüklenmemesi PASS. CK antetli sürekli temel ve aynı çizimde ikinci çalışma COMPLETE; mevcut beton/donatı/etiketler korunur, exit=0. Kullanıcı kaynak çizimi kaydedilmedi.
- İlk sandbox Core Console denemesi DLL yüklemeden önce profil oluşturma hatasıyla durdu; izinli ayrı test sürecinde kontroller geçti. Kullanıcının çalışan AutoCAD oturumuna DLL yüklenmedi. Canlı koyu tema yerleşimi/tıklama ve tüm çizim komutları kullanıcıda doğrulanmalı.

## Teslim

`NETLOAD/GUNCEL_2027/ST4_Plan_ID_Ciz_2027_20261023_205205.dll` — yayın betiği metadata/bağımlılık/kopya SHA256 doğrulaması PASS. Aynı pakette Claude'un son **ST4_Yardimci_2027_20261023_204332.dll** dosyası SHA256 aynı kalarak korunur. Eski paket NETLOAD/arsiv içindedir; masaüstü kısayolu aynı GUNCEL_2027 klasörünü açar.

Claude'un döşeme kaydı tamamlandı ve ayırmaları kalktıktan sonra entegrasyon/dağıtım yapıldı; döşeme kaynakları değiştirilmedi. net48/ofis/setup bu işte derlenmedi veya dağıtılmadı. Commit/push yapılmadı.
