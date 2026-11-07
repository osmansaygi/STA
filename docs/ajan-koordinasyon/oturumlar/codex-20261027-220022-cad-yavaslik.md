# Codex — AutoCAD / STA4CAD yavaşlık incelemesi
- Ajan: Codex / bu sohbet.
- Güncelleme: 27.10.2026 22:02 Europe/Istanbul.
- Durum: tamamlandı — uygulanabilir salt okunur inceleme bitti; tüm ayırmalar serbest.
- Görev: sistem ve CAD yavaşlığını salt okunur incelemek.
- Ayrılan: bu kayıt; docs/modul-durum/SistemStabilitesi20261024.md yalnız 27 Ekim tanılama eki.
- Kontrol: son iki günün tüm kayıtları, eski beklemedeki ilgili kayıtlar, menü katalogları/Ribbon durumu, kurulu otomatik yükleyici hedefi ve NETLOAD/GUNCEL_2027 kontrol edildi. Güncel Plan 20261027_133752 + Yardımcı 20261026_205437. Diğer ajan alanları/ortak çıktılar değişmez.
- Bulgular: 128 GB RAM'in yaklaşık 99 GB'ı boş; PagesInputPersec=0; kısa disk örneğinde kuyruk ve etkinlik 0. Etkin geçici stabilite planında AC PERFBOOSTMODE=0 doğrulandı; frekans sayacı 2000 MHz (tüm çekirdeklerin yük altı hızını kanıtlamaz). CPU CIM yükü %16; kısa süreç farkında baskın toplam CPU tüketimi yok.
- Sınır: iki ayrı gerçek Windows süreç sorgusunda AutoCAD/STA4/PROG2 ve kurulum yolu eşleşen uygulama bulunmadı. Uygulama içi yavaşlık yeniden üretilemedi. Sistem ayarı, uygulama ve DLL değişmedi.
- Bugün: üç WHEA17 PCIe kaydı; WHEA18/19 yok. Tarihsel çekirdek kararsızlığı nedeniyle turbo kendiliğinden açılmadı.
- Doğrulama: Windows CIM, süreç farkı, güç ayarı geri okuma, olay günlüğü. C# testi/derleme/DLL kopyalama/canlı AutoCAD testi yok.


## Devam — STA4CAD analiz hızını düzeltme
- Güncelleme: 27.10.2026 22:05 Europe/Istanbul.
- Durum: aktif.
- Kullanıcı yetkisi: STA4CAD analizini detaylı incele ve düzelt.
- Ayrılan: tools/Sta4Performance20261027/** (tanı, güç ayarı yedeği ve geri alma); docs/modul-durum/SistemStabilitesi20261024.md yalnız bu devam eki; bu kayıt.
- Diğer ajan kayıtları yeniden kontrol edildi; bu alanlarda çakışma yok. STA4/AutoCAD kaynakları, model/analiz seçenekleri, NETLOAD ve ortak derleme değişmez.
- Plan: yük altı ölçüm; geçici stabilite planı değişmeden ayrı kopyada standart boost ve sınırlı maksimum frekansla geri alınabilir düzenleme; donanım olayı izlemesi. Hız artışı gerçek analiz süresiyle doğrulanmadan başarılı sayılmaz.
- Sınır: iki güncel sorguda STA4 analizi açık değil; kullanıcıya projeyi açıp analizi başlatması soruldu, yanıt bekleniyor.
- 22:07 sonuç: ayrı Kontrollu 4GHz planı etkin, eski stabilite planı ve 8192 bayt yedeği korunuyor. AC Boost=1, iki sınıf maxfreq=4000; 95 işlemci ayarında tam 3 beklenen AC fark. İlk export yetki hatası sonrası UAC ile başarılı.
- Kısa seri PowerShell kontrolü ortanca 1885→841 ms (2.24 kat); STA4 analiz ölçümü değildir. Yeni WHEA kısa gözlemde 0; sıcaklık/uzun vadeli kararlılık doğrulanmadı.
- Kanıt/geri alma tools/Sta4Performance20261027 altında. İki betik syntax PASS; Git dışı kanıt kontrolü PASS. Model/analiz ayarları, C# kaynakları ve DLL değişmedi; proje testi/derleme/DLL kopyalama/gerçek AutoCAD testi yok.
- Durum: beklemede — güç düzeltmesi uygulandı; kullanıcı STA4 analizini başlatınca gerçek yük ve süre ölçümü yapılacak. Ortak dosya/çıktı rezervasyonları serbest; yalnız bu sohbete özel tools/Sta4Performance20261027/** alanı ayrılı kalır.
