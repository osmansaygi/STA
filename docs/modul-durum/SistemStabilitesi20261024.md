# 24.10.2026 — bilgisayar çökmesi, sistem incelemesi

## 26.10.2026 — yavaşlık için salt okunur kontrol

12:31–12:35 ölçümü: 127,7 GB RAM / yaklaşık 107 GB boş; bellek sayfalama okumaları 0. C: 1489 GB, D: 1314 GB boş; iki fiziksel SSD Healthy/OK. Kısa örneklerde disk kuyrukları 0, etkinlik %0–5. İlk CIM CPU yükü %59; sonraki dört toplam CPU örneği %10/%10/%17/%0, bu yüzden sürekli toplam CPU doygunluğu gösterilmedi. Süreç sayaçları çekirdek toplamıdır: C:/STA4V14/PROG2.EXE %95–98 ile yaklaşık bir mantıksal işlemciyi kullanıyor; AutoCAD aralıklı yük üretiyor. PROG2 işlemi sonlandırılmadı.

BEYKENT - Gecici Stabilite planı halen etkin; AC PERFBOOSTMODE=0 (turbo kapalı) doğrulandı. Anlık frekans sayacı 2000 MHz; bu tek örnek tüm çekirdeklerin sabit hızını veya ısıl kısıtlamayı kanıtlamaz. Önceki kararlılık önlemi işlemci yoğun/tek iş parçacıklı işleri yavaşlatabilir; kesin süre farkı için karşılaştırmalı test yapılmadı. Ayar geri alınmadı.

Bugünkü 11:48:36 açılışından sonra üç WHEA17 (NVIDIA PCIe/ses uç noktaları), disk hatası ve WER BugCheck kaydı 0. 25 Ekim 17:58 tarihli Harddisk2/DR2 denetleyici hatası var; bugün yalnız fiziksel disk0/1 görünüyor, önceki aygıt kesin eşlenemedi. WHEA17 kayıtları bugünkü yavaşlığın nedeni olarak kanıtlanmadı. CPU sıcaklığı bu sorgularda sunulmadı; fiziksel donanım sağlamlığı doğrulanmadı.

Sistem ayarı, uygulamalar ve proje/DLL değişmedi; yalnız bu durum notu ve sohbete özel koordinasyon kaydı güncellendi. Test/derleme/DLL kopyalama/canlı AutoCAD testi bu salt okunur tanılamada uygulanmadı.

## Sonuç ve kanıt sınırı

Dell Precision 3680 / Intel Core i9-14900 sisteminde işlemci/platform kararsızlığına güçlü kanıt var. Kesin arızalı parça uzaktan belirlenmedi; işlemci değişimi gerektiği bu kontrollerle tek başına kanıtlanmaz. Güncel mikrokoda rağmen tekrarlayan çekirdek hataları nedeniyle Dell donanım incelemesi önceliklidir.

- Son 30 günlük System günlüğü: 13 hata kodu içeren BugCheck raporu, 15 Kernel-Power 41, 5 WHEA19 işlemci çekirdeği Internal parity error. Beş hatanın dördü APIC 33, biri APIC 32. Tarihler: 29.09, 04.10, 22.10, 23.10 (iki kez).
- 24.10 15:51: BugCheck 0x20001. Windows hata kovası: NOBLOB_HYPERVISOR_ERROR_STACK_BUFFER_OVERRUN_IMAGE_hvix64.exe. Bu kova tek başına Hyper-V yazılımının kusurlu olduğunu kanıtlamaz.
- Diğer mavi ekranlar 0x3B ve 0x34. FLTMGR adı geçen raporlar var; bu sistem dosyasını silmek/değiştirmek uygun çözüm değildir. Filtre sürücüsü listesi kaydedildi.
- 96 WHEA17 kaydı NVIDIA GPU/ses PCIe uç noktalarını gösteriyor; örnekler açılış zamanlarında. Ekran kartı arızası veya son mavi ekranın nedeni olarak kesinleştirilmedi.
- AutoCAD yanında csc.exe, mscorsvw.exe ve Core Console da erişim ihlali/yürütme motoru hataları veriyor. AutoCAD/ST4 kodu bütün çökmeleri açıklamaz.

## Donanım ve ayarlar

BIOS 1.29.0 (27.07.2026), işlemci mikrokodu 0x133; Intel'in önerdiği 0x12F'den eski değil. BIOS'un bugün en son sürüm olduğu doğrulanmadı. 4×32 GB DDR5, yapılandırılmış hız 3600 MT/s; pazarlanan 5600 değerine zorlanmadı. Bellek yeterli, pagefile otomatik. İki Timetec 2TB SSD Healthy; C'de yaklaşık 1,58 TB, D'de 1,42 TB boş. SMART sıcaklıkları 42/27°C, wear 0; hata sayaçları sürücü tarafından sunulmadığından yokluğu sıfır hata kabul edilmedi. NVIDIA anlık boşta ölçüm 31°C, 3,70 W (sınır70W). CPU sıcaklığı ve fiziksel RAM sağlamlığı henüz doğrulanmadı. Dell BIOS WMI ayar sağlayıcısı bu oturumda okunamadı; BIOS içindeki varsayılan güç/overclock ayarları doğrulanmadı. PnP ConfigManagerErrorCode sorgusunda sorunlu aygıt sayısı 0.

Windows 11 Pro 26H2 / 26300.9711. NVIDIA 32.0.15.9730, Intel GPU 32.0.101.7088. Sürücülerin en güncel veya sorunsuz olduğu yalnız bu bilgilerle söylenemez.

## Uygulanan geri alınabilir önlem

Mevcut Dengeli plan dışa aktarıldı; ayrı `BEYKENT - Gecici Stabilite` planı etkin:
- Yalnız şebeke/AC işlemci PERFBOOSTMODE=0: Turbo Boost devre dışı. İşlemci ağırlıklı işlerde performans düşebilir. Etkin donanım frekans sınırı ayrıca ölçülmedi.
- Yalnız AC PCIe ASPM=0: bağlantı güç tasarrufu kapalı.
- CrashDumpEnabled 3→7 (otomatik bellek dökümü), AlwaysKeepMemoryDump 0→1. AutoReboot zaten 0 idi; korundu. Büyük dökümler disk alanı kullanabilir ve özel çalışma verisi içerebilir; dışarı yüklenmedi.
- Önceki güç planı ve kayıt değerleri `settings-before.json`, `original-power-plan.pow` içinde. Yönetici PowerShell'de `Undo-StabilityMitigation.ps1` önceki planı ve iki kayıt değerini geri getirir.

Bu önlem deneysel risk azaltmadır; donanım onarımı veya çökmezlik kanıtı değildir. TPM temizlenmedi, sanallaştırma/güvenlik kapatılmadı, BIOS güncellenmedi, kullanıcı uygulamaları kapatılmadı ve yeniden başlatma yapılmadı. Önceki sohbetin HKCU DOTNET_TieredPGO=0 ayarı değiştirilmedi.

## AutoCAD sürüm kontrolü

Son GUI dökümü `acad.exe.34360.dmp` (19:01): Plan `NETLOAD/GUNCEL_2027/ST4_Plan_ID_Ciz_2027_20261024_185511.dll`, Yardımcı `ST4_Yardimci_2027_20261023_213059.dll`; net48 değil, doğru 2027 ailesi. Güncel disk paketi Plan195554 + aynı Yardımcı213059. Dökümdeki Plan sonradan yayımlanan sürümden eski, ancak bu sürüm farkı çökme nedeni olarak kanıtlanmadı. Döküm 0xc0000005; .NET10.0.12/coreclr ve clrjit yüklü. Otomatik yükleyici mevcut GUNCEL_2027 klasörünü hedefliyor. Menü/ribbon düzeni okundu; proje kaynağı, DLL ve yükleyici değiştirilmedi. Derleme, DLL kopyalama, gerçek AutoCAD testi yapılmadı; CPU kararsızlığı sürerken ek yoğun çizim stresi uygulanmadı.

## Windows doğrulaması

DISM RestoreHealth /NoRestart başlatıldı; ardışık SFC /scannow ve C/D chkdsk /scan betiğe alındı, DISM sonrası çalışacak. 20:06 CBS/CSI günlüğü numCorruptions=288 bildirdi; onarım içeriği Windows Update üzerinden alınıyor. DISM 20:31 itibarıyla başarıyla tamamlandı. Nihai CBS özeti: 288 CSI Payload + 299 CSI FileFlags = 587 bozulma tespit edildi ve tamamı onarıldı; Operation result=0x0, yeniden başlatma gerekmedi. SFC 20:34 itibarıyla bozuk sistem dosyaları buldu ve başarıyla onardı (Repair complete); çıktı sfc-readable.txt içinde. C ve D CHKDSK /scan dosya sistemi sorunu bulmadı, iki taramada kayıtlı kötü sektör 0KB; bu fiziksel yüzey testi değildir. 20:36:19 admin-result.json: DISM=0, SFC=0, CHKDSK C=0, CHKDSK D=0. Tüm komutlar tamamlandı; arka planda bekleyen bakım komutu yok. Resmi Windows Update taraması tamamlandı: yalnız Defender KB2267602 imza güncellemesi 1.461.184.0 sunuluyor; kurulum yapılmadı. Bu taramada Windows/sürücü güncellemesi sunulmadı; OEM kataloglarının tamamını kapsamaz. Ayrı ek envanter betiğinin UAC isteği kullanıcı tarafından iptal edildi; zorlanmadı. Salt okunur tarama ve Defender/başlangıç denetimi standart kullanıcı oturumunda tamamlandı. Defender antivirüs/gerçek zamanlı koruma açık; son hızlı tarama bugün 16:52.

## Kalan somut işler / servis notu

Açık çalışmalar kaydedildikten sonra kontrollü yeniden başlatmada Dell F12 Diagnostics üzerinden genişletilmiş işlemci/bellek tanılaması gerekir. İşletim sistemi içindeki SMART/boş bellek kontrolü bu testin yerine geçmez. Intel varsayılan BIOS güç ayarları ve sistem için Dell'in güncel BIOS/sürücü uygunluğu servis tarafından kontrol edilmeli. Güncel BIOS sonrası devam eden WHEA19/APIC33 hatalarıyla Dell'e işlemci/platform incelemesi ve gerekiyorsa garanti kapsamında parça değişimi talebi iletilmeli. Kullanıcı adına servis mesajı gönderilmedi.

Resmi kaynaklar:
- Dell, i9-14900 ve Precision3680'i etkilenenler arasında listeliyor; BIOS sonrası devam eden çökmelerde Dell desteğini öneriyor: https://www.dell.com/support/kbdoc/en-us/000227933/industry-wide-issue-with-intel-core-13th-and-14th-generation-i5-i7-and-i9-processors
- Intel, varsayılan güç ayarları ve 0x12F veya sonraki mikrokodu öneriyor: https://www.intel.com/content/www/us/en/support/articles/000102331/processors.html
- Microsoft PERFBOOSTMODE: https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-perfboostmode

Tüm yerel kanıt ve geri alma dosyaları: `tools/SystemStability20261024/`.





## Son doğrulama / teslim

Dört bakım betiği PowerShell sözdizimi kontrolünde 0 hata. Etkin geçici plan GUID b0836c27-d1f1-4926-a91a-fd9025e11d6d; AC Boost=0, AC ASPM=0; dump7/keep1/autoReboot0 geri okuma ile doğrulandı. Eski Dengeli plan korunuyor, dışa aktarma yedeği8192 bayt. Geri alma betiği çalıştırılmadı; geri alma yapılırsa geçici önlem devreden çıkar. Tanılama klasörü kendi .gitignore dosyasıyla Git dışı; iki örnek özel kanıt yolunda git check-ignore doğrulandı. Windows onarımının her çöküşü giderdiği iddia edilmez; donanım arızasının hangi parçada olduğu ve uzun vadeli stabilite henüz doğrulanmadı. Kullanıcı uygulamaları yeniden başlatılmadı. Proje regresyonu/derleme/DLL kopyalama/canlı AutoCAD testi yapılmadı (üretim kodu değişmedi; görev sistem bakımı).


## Dell garanti başvurusu için teslim
24.10.2026: BIOS Service Tag BRNWX64 salt okunur doğrulandı. Kullanıcının devam eden garanti beyanıyla output/pdf/Dell_Ariza_Kaydi_20261024 altında iki sayfalık PDF arıza raporu, kısa destek başvuru metni ve seçilmiş kanıtları içeren ZIP hazırlandı. PDF'nin iki sayfası görsel incelendi, metin/sayfa sayısı ve ZIP bütünlüğü doğrulandı. Ham DMP ZIP'e eklenmedi; yerelde tutuluyor. Garanti kapsam/bitiş tarihi Dell portalından teyit edilmedi, rapor bunu açıkça belirtiyor. Dell'e başvuru yapılmadı veya dosya gönderilmedi. Bu devamda sistem/BIOS/proje/DLL ayarı değiştirilmedi. Word render motoru mevcut olmadığından teslim PDF + düzenlenebilir düz metindir.

## 27.10.2026 — AutoCAD / STA4CAD yavaşlık kontrolü

22:00–22:02 Europe/Istanbul, salt okunur Windows ölçümü: 127.7 GiB kullanılabilir fiziksel bellek kapasitesinin yaklaşık 99 GiB'ı boş, PagesInputPersec=0. Kısa disk örneğinde C/D kuyrukları ve etkinlik 0; C yaklaşık 1.57 TB, D 1.41 TB boş. CPU CIM yükü %16; kısa süreç örneğinde toplam işlemciyi tek başına dolduran süreç görülmedi. Bu boşta örnekleri işlem sırasında darboğaz olmadığını kanıtlamaz.

Etkin BEYKENT - Gecici Stabilite planında şebeke AC PERFBOOSTMODE=0, turbo kapalı; powercfg /qh ile doğrulandı. Frekans sayacı 2000 MHz; tüm çekirdeklerin sürekli/yük altı hızını kanıtlamaz. Önceki çökme nedeniyle kapatılan turbo işlemci ağırlıklı CAD işlerinde yavaşlığa katkı verebilir; süre karşılaştırması yapılmadı. Microsoft PERFBOOSTMODE belgesi 0 değerini Disabled olarak doğrular: https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-perfboostmode . Önceki WHEA19 çekirdek kararsızlığı nedeniyle güç ayarı değiştirilmedi.

İki ayrı gerçek Windows süreç sorgusunda acad/STA4/PROG2 veya AutoCAD/Sta4 kurulum yoluyla eşleşen uygulama bulunmadı. Bu nedenle uygulama içi yavaşlık yeniden üretilemedi; sonraki ölçüm uygulamalar açıkken, kullanıcı yavaşlayan işlemi yaparken gerekir. Bugün WHEA17 PCIe sayısı 3; WHEA18/19 yok. PCIe kaydı bu yavaşlığın nedeni olarak kanıtlanmadı. CPU sıcaklığı ölçülmedi.

Koordinasyon/menü/yükleyici kontrolü: güncel Plan 20261027_133752 ve Yardımcı 20261026_205437; kurulu bundle GUNCEL_2027 hedefliyor. Sistem ayarı, uygulamalar, kaynak ve DLL değişmedi. C# testi, derleme, DLL kopyalama ve canlı AutoCAD komut doğrulaması yapılmadı (tanılama görevi). Yalnız durum ve sohbete özel koordinasyon kaydı güncellendi.

## 27.10.2026 22:07 — STA4CAD analizi için kontrollü güç düzeltmesi

Kullanıcı detaylı inceleme ve düzeltme istedi. STA4CAD 14.01 (PROG2 16.10.2026, FEASTA 20.10.2026) kurulu. Tekrarlanan gerçek Windows süreç sorgularında C:\Sta4V14 altında çalışan uygulama/analiz bulunmadı; kullanıcıdan yavaş projeyi açıp analizi başlatması istendi. STA4 modeli, mesh, yük kombinasyonu, analiz seçenekleri veya program dosyaları değiştirilmedi. Uyumluluk Layers kayıtlarında STA4 için kural görülmedi; IFEO kök listelemesi erişim hatası verdi, ilgili PROG2/FEASTA sorguları değer döndürmedi; tüm kısıtların yokluğu kanıtlanmadı. CPU sıcaklık sağlayıcısı veri döndürmedi.

Uygulandı: eski BEYKENT - Gecici Stabilite planı değişmeden korundu ve 8192 bayt .pow yedeği alındı. Ayrı BEYKENT - STA4CAD Kontrollu 4GHz planı (b2c0b8f7-dce4-44db-9f46-5c993a641e40) etkinleştirildi. Yalnız şebeke AC PERFBOOSTMODE 0→1, PROCFREQMAX/PROCFREQMAX1 0→4000 MHz. İşlemci alt grubunda 95 ayar karşılaştırıldı: tam olarak bu üç AC değer farklı; DC değerler aynı. Diğer güç alt grupları klon üzerinden korundu; ayrıca tek tek karşılaştırılmadı. 4 GHz, Windows'a yapılandırılan üst sınırdır; gerçek yük altı çekirdek hızının/ısısının veya donanım güvenliğinin ölçümü değildir. Önceki WHEA19 nedeniyle sınırsız turbo yerine bu sınırlı deneme seçildi; uzun dönem kararlılık kanıtı yok.

İlk standart kullanıcı denemesi /export ayrıcalığında durdu; hiçbir güç ayarı değişmedi. Ardından kullanıcı tarafından Windows UAC ile yükseltilmiş betik çalıştı. Aynı PowerShell oturumunda ısınma turu dışarıda bırakılarak 3'er kısa tek iş parçacıklı döngü: önce 1885/1815/2026 ms, sonra 841/846/831 ms; ortanca 1885→841 ms, oran 2.24. Son checksum aynı. Bu sentetik PowerShell kontrolüdür; STA4CAD analizi veya matematiksel sonuç doğrulaması değildir ve STA4 hızlanma oranı olarak sunulmaz. İşlemciye ait nominal 2000 MHz sayacı değişiklikten sonra da aynı kaldı; önceki 2000 sayısı tek başına gerçek frekans sınırını göstermiyor.

22:06:06–22:06:55 arası yeni WHEA olayı 0; kısa gözlem uzun vadeli stabiliteyi doğrulamaz. Yaklaşık 99 GiB RAM boş, sayfalama okuması 0; iki SSD Healthy/OK, disk kuyrukları 0. Analiz çalışmadığı için yük altı CPU/çekirdek dağılımı, disk erişimi ve gerçek çözüm süresi hâlâ açık. Çalışan analiz görülünce aynı projenin öncesi/sonrası ölçümü gerekir.

Kanıt ve geri alma: tools/Sta4Performance20261027/result.json, followup.json, setting-differences.json, power-before.json, previous-plan.pow; Apply-ControlledPower.ps1 / Undo-ControlledPower.ps1 sözdizimi PASS. Undo yalnız önceki güç planına döner; dump ve diğer stabilite kayıt ayarlarına dokunmaz; henüz çalıştırılmadı. Dosyalar Git dışında doğrulandı. NETLOAD/AutoCAD kaynağı değişmedi; C# proje testi/derleme/DLL kopyalama/canlı AutoCAD testi uygulanmadı. STA4CAD gerçek analizi henüz doğrulanmadı.

Resmi ayar tanımları: https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-perfboostmode ve https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-maxfrequency .
