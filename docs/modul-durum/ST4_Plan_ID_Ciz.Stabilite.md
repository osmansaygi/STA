# Plan ID — 23.10.2026 tekrarlayan çökme takibi

## SurekliTemelGorunus.cs:556 Commit — kod incelemesi, 23.10.2026 (Claude)

- Döküm yığını: çökme `Transaction.Commit` → native `AcDbImpTransaction.transactionManager` ilk adımında; yani işlem nesnesinin kendisi okunurken. Commit satırına özgü bir işlem değil, önceden bozulmuş belleğin belirtisi.
- Kod taraması: sürekli temel görünüş/donatı dosyalarında yeni varlıkların tamamı `AppendEntity` ile ekleniyor (geçersiz geometri Dispose), veritabanına eklenmeyen geçici DBObject, Explode/OffsetCurves/Clone yok. Kiriş yardımcılarında yeni DBText `GeometricExtents` (KirisAcilimYaziKutusu*/KolonYaz/GorunusEtiket) hâlâ var; font motoru şüphesi eski kayıtta (acdb26+0x1e3131) kanıtlanamamıştı.
- Core Console 2027, Plan 205205 + Yardımcı 213059, CK_20827_2_01: `DOTNET_HeapVerify=1`, küçük gen0 (sık GC), antetsiz 10/10 + antetli 10/10 COMPLETE, exit=0 (`tools/DosemeY20261023/surhv_a0.*`, `surhv_a1.*`). Önceki 3/3 normal koşu da geçti. Yönetilen yığın bozulması yakalanmadı.
- Sonuç: deterministik kod hatası bulunamadı; kod değiştirilmedi. Donanım bulgusuyla birlikte en olası neden işlemci kararsızlığı. Önceki sohbette bu stres testi sırasında bilgisayarın tamamen çökmesi de bununla uyumlu.

## Donanım bulgusu — 23.10.2026 21:50 (Claude)

Çökmelerin ana nedeni eklenti kodu değil, makinenin işlemcisi görünüyor. Kod/DLL değiştirilmedi.

- Makine: Dell Precision 3680, Intel Core i9-14900, BIOS 1.29.0 (07.2026), mikrokod 0x133.
- `System` günlüğü WHEA-Logger 19: "Corrected Machine Check, Internal parity error, İşlemci Çekirdeği, APIC 33" — 24.08'den beri 11 kez, son 23.10 21:28 (21:35 acad çökmesinden 7 dk önce). 14. nesil Intel bozulma (Vmin kayması) imzası. Ayrıca 99 adet WHEA 17 (NVIDIA PCIe AER, düzeltilmiş).
- 30 günde 13 Kernel-Power 41 + 13 olay 6008 (beklenmedik kapanma) + 12 BugCheck 1001 (mavi ekran).
- 7 günde `Application Error`: acad 78, accoreconsole 47, mscorsvw 55, csc 37, VBCSCompiler 11, MsMpEng (Defender motoru) 8, svchost, SearchIndexer, chrome, pwsh/powershell clrjit, RADSTA/RADSON. Eklentiyle ilgisi olmayan .NET ve native süreçler aynı biçimde (erişim ihlali, coreclr/clrjit/clr) çöküyor.
- 21:35 acad çökmesi açılıştan 24 s sonra, hiçbir ST4 DLL yüklü değilken (dotnet-dump: ST4 modülü yok, yönetilen komut yok, ExecutionEngineException). 21:32 Core Console çökmesi aynı DLL/girdinin tekrarında oluşmadı (deterministik değil).
- 18:47 ve 20:59 acad dökümlerinde yığın `SurekliTemelGorunus.cs:556 Transaction.Commit` (ExecutionEngineException). Ağır ayırma yapan bu komut bozuk donanımda daha sık yakalanıyor olabilir; donanım düzelmeden kod hatası olarak kanıtlanamaz.

Öneri (kullanıcı/IT): Dell/Intel garanti başvurusu (14. nesil uzatılmış garanti), Intel Processor Diagnostic Tool, geçici olarak güç planında "en yüksek işlemci durumu" %99 (turbo kapalı) veya BIOS'ta Intel varsayılan güç profili; AutoCAD SAVETIME kısa tutulmalı. Donanım düzeldikten sonra tekrar eden çökme olursa döküm yığını yeniden incelenmeli.

## Güncel ortak teslim — 23.10.2026 17:44

Kullanıcının isteğiyle tek güncel seçim adresi `NETLOAD/GUNCEL_2027/` oldu. İçinde güncel Plan `ST4_Plan_ID_Ciz_2027_20261023_172809.dll` ve yeni derlenen Yardımcı `ST4_Yardimci_2027_20261023_173637.dll` var. Masaüstü ve proje kökündeki `GUNCEL DLL - AutoCAD 2027.lnk` burayı açar. Aşağıdaki 16:44 sürüm bilgisi tarihsel kayıttır. Yeni teslimde `tools/Publish-CurrentNetload2027.ps1` iki net10/2027 modülünü ve ortak bağımlılıkların hash eşitliğini denetleyerek sabit klasörü yeniler; eski paket arşivlenir. Yanlış net48 girdisinin mevcut paketi değiştirmeden reddedildiği sınandı.

Yardımcı ve native paket koşucusu izole derlemeleri 0 hata/0 uyarı; Plan yeniden derlenmedi, son 172809 alındı. İki eklenti doğrudan aynı klasörden NETLOAD edildi; yüklü modüllerin ve NTS'nin bu klasörden geldiği doğrulandı. Antetli CK sürekli temel ve aynı çizimde ikinci çizim COMPLETE, eski nesneler korundu, exit=0. Startup regresyonları PASS. Kanıtlar `tools/CurrentNetload2027/` altında build.txt, publish.txt, package-probe.txt, combined-final-repeat.txt ve combined-final.console.log. Canlı AutoCAD arayüzü denenmedi. İlk ek yansıma tanılama denemelerinde coreclr.dll+0x3596cf erişim ihlali görüldü; ardından daraltılmış paket kontrolü ve gerçek çizim tamamlandı. Sistemsel kararsızlık açık kalır.

## Kanıt ve düzeltme

- 16:35:06 AutoCAD 2027 çökmesi: `acad.exe.26672.dmp` modül listesinde `NETLOAD/ST4_Plan_ID_Ciz_20261023_163210.dll` var. Bu net48 derlemesidir; AutoCAD 2027 API/net10 derlemesi değildir. 16:22 dökümünde de önceki net48 161132 yüklenmiş. Son çizim değişiklikleri geri alınmadı.
- Yanlış seçilen 163210 DLL/PDB, SHA256 doğrulanarak bağımlılıklarıyla `NETLOAD/Legacy-net48/` altına ayrıldı. Kökten kaldırılan iki dosyanın yedeği `tools/CrashFix20261023/` içinde. Kök bağımlılıklar/Yardımcı dosyaları korunur.
- `ST4_Plan_ID_Ciz.csproj` net48 kopyalama hedefi artık yalnız `Legacy-net48` altına yazar. Konsol yükleme adresi sürümü açık belirtir. 2027 otomatik dağıtımı kapalı kalır.
- AutoCAD 2027 için güncel doğru dosya: `NETLOAD/AutoCAD2027/ST4_Plan_ID_Ciz_2027_20261023_163103/ST4_Plan_ID_Ciz_2027_20261023_163103.dll`. SHA256: `E25A1A440E7933DD763C2EDD84D59912F041E126275D5A1FA5B34C5BDE266650`. Bu DLL değiştirilmedi; yeniden eklenti derlenmedi.

## Doğrulama

- Gerçek MSBuild `CopyToNetloadFolder` hedefi izole kökte çalıştırıldı: net48 DLL/PDB ve bağımlılıkları Legacy-net48 altında, kök değişmedi, hash aynı. 2027 koşulunda hedef hiçbir dağıtım klasörü oluşturmadı. `pwsh -NoProfile -File tools/Test-PlanDeployment.ps1` PASS.
- İzole native test koşucusu net10.0-windows: derleme 0 hata/0 uyarı. Doğru 2027 DLL'siyle Core Console 2027: CK antetsiz, CK antetli, CK aynı çizimde tekrar, MS, AKDAG, sentetik/verisiz toplam 6/6 COMPLETE. Tekrar akışında eski beton/donatı/etiketlerin korunduğu kontrolü PASS. Kaynak ST4/DWG kaydedilmedi; kullanıcı oturumu kullanılmadı.
- ST4 NETLOAD çağırmadan üç ayrı Core Console açılış/kapanış: 3/3 exit=0. Canlı AutoCAD arayüzünde açılış veya formdan çizim denenmedi.
- Genel `Verify-Project.ps1`: Yardımcı/STA startup PASS; daha önce kayıtlı `Test-BaDimensionStyles.ps1` fixture eksik `BeykentOrtak` / CS0246 hatasında durdu. Sonraki TemelDetayGeometry ve TemelIlaveKutuBol ayrı çalıştırıldı, exit=0. Genel doğrulama tümüyle başarılı değildir.
- Kanıtlar: `tools/CrashFix20261023/` altındaki dump-modules.txt, windows-events.json, distribution.json, native-build.txt, native-summary.txt, ck-repeat.txt, startup-summary.txt, deployment-test.txt ve verify.txt.

## Açık somut kısıt

16:34 çökme dökümünün modül listesinde ST4 eklentisi yok; .NET 10.0.12 içinde AccessViolation görülüyor. Aynı zaman aralığında bağımsız csc.exe ve .NET Framework mscorsvw.exe süreçleri de çökmüş. Dolayısıyla yanlış DLL dağıtımı kanıtlı bir sorun olsa da tüm açılış çökmelerinin tek nedeni olarak sunulamaz. İşletim sistemi/çalışma zamanı/donanım ayrımı bu kayıtlarla kesinleşmedi; sistem ayarı veya runtime kurulumu değiştirilmedi. Doğru DLL ile bu turda hata yeniden oluşmadı; canlı açılış ve sürekli temel kullanıcı doğrulaması açık.
