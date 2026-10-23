# Ortak klasörde Cursor, Codex ve Hermes çalışma protokolü

## Başlangıç

Her görevin başında bu dosyayı ve `oturumlar/` altındaki kayıtları oku. İlgili modül için `docs/modul-durum/INDEX.md`, modül durum dosyası ve ilgili `.cursor/rules/` teknik kurallarını oku.

Her sohbet kendine benzersiz bir kayıt açar: `oturumlar/<cursor-codex-veya-hermes>-<YYYYMMDD-HHmmss>-<benzersiz-ek>.md`. Aynı araçta birden fazla sohbet de ayrı kayıt tutar. Kayıt saatleri Europe/Istanbul saat dilimindedir. Yalnız kendi kaydını güncelle.

Kaydın içeriği: ajan, sohbet/görev tanımı, güncelleme zamanı, durum (aktif/beklemede/tamamlandı), düzenlenecek tam proje göreli dosya yolları, ortak çıktı kaynakları, yapılan değişiklik, doğrulama ve açık işler. Kayıtları kısa tut.

## Düzenleme öncesi

1. Hedef dosyaları kendi kaydına yaz. Henüz belirlenmemiş dosyalarda değişiklik yapma; kapsam genişlerse önce kaydı güncelle.
2. Diğer kayıtları tekrar oku. Aktif veya beklemede kaydın ayırdığı dosya/kaynak çakışıyorsa o dosyayı değiştirme. Aynı anda kapsam bildirildiğini görürsen dur; sessizce öncelik varsayma. Bağımsız işe devam et, çakışmayı kullanıcıya bildir.
3. Dosyayı değiştirmeden hemen önce diskten tekrar oku. Önceki okumadan beri değişmişse değişikliği değerlendirerek güncel içerik üzerine çalış; başka ajanın değişikliğini silme veya geri alma.
4. Görev sınırında ve uzun işlerde anlamlı aşamalarda kayıtları tekrar kontrol et. Sürekli dosya tarama veya boşta sorgulama döngüsü çalıştırma.

Kayıtlar işbirliği amaçlıdır; atomik dosya kilidi sağlamaz. Aynı dosyaya paralel yazım gerekiyorsa ayrı Git worktree/branch kullan ve değişiklikleri kontrollü birleştir. Kaydın eski olması onu otomatik geçersiz yapmaz; sahipliğini doğrulamadan kaldırma.

## Derleme, DLL ve ortak işlemler

- Aynı projede `bin/`, `obj/`, ortak `_build_tmp/`, `NETLOAD/` veya aynı dağıtım DLL'sine yazacak işler ortak kaynaktır. Kullanacağın gerçek çıktı yollarını kendi kaydında belirt; aynı kaynakta tek ajan işlem yapar.
- Kaynak dosyaları farklı olsa bile aynı projenin ortak çıktısını eşzamanlı derleme. Ayrı çıktı ve ara çıktı yolları kullanılıyorsa bunları açıkça kaydet ve gerçekten ayrıldığını doğrula.
- Ortak Git index/branch işlemleri de sırayla yapılır; başka ajanın dosyalarını commit'e katma. Kullanıcı değişikliklerini geri alma.
- Derlendi, DLL kopyalandı ve AutoCAD içinde denendi bilgilerini ayrı yaz. AutoCAD doğrulaması yapılmadıysa başarılıymış gibi gösterme.

## İş sonu ve devir

Kendi kaydına değişen dosyaları, doğrulama sonucunu ve açık işi yaz. İlgili modül durum dosyasını kısa güncelle; bu dosyayı da değişiklik kapsamına alıp çakışma kontrolü yap. İş tamamlanınca durumu tamamlandı yap ve dosya/kaynak ayırmalarını kaldır. Yarım işte beklemede durumunu kullan; hangi dosyaların ayrılmaya devam ettiğini açıkça yaz. Başka ajanın kaydını kapatma.

Bu protokol sohbetleri veya niyetleri otomatik paylaşmaz; kaydedilmemiş editör değişiklikleri diğer araca görünmez. Ortak bilgi diskteki dosyalar ve kayıtlardır. Kullanıcı talimatları önceliklidir.
