# Radye karşılaştırma — ilk sürüm

**GERİ ÇEKİLDİ:** Kullanıcı yalnız SO1 noktasal ısı haritası çözüldükten sonra komut istedi. Bu klasör araştırma/prototip kaydıdır; aşağıdaki komut ve dağıtım açıklamaları tarihsel denemeyi anlatır. Komut/panel kaydı kaldırıldı, standart NETLOAD'da 161449 sürümü geri kondu. Noktasal As çözülmeden bu aktarım prototipi yeniden teslim edilmemeli.

Komut: `RADYETEMELPLANIOTOMATIK`, kısa adı `RADYEOTOMATIK`. BAPANEL içinde ayrı Radye Temel Planı Otomatik düğmesi vardır. ST4 projesi seçilir, çizimde sol-alt yer seçilir. Varsayılan seçenek STA referansını ve BEYKENT katmanlarına dönüştürülen kopyayı yan yana yerleştirir.

Bu sürüm ısı haritasından yeni donatı hesaplamaz; mevcut RADYETEMEL/RADYEILAVE algoritmasını çağırmaz. STA'nın MatTem.dwg içinde kaydettiği şematik ilave çizgileri ve etiketlerini aktarır. RAD ağ/ölçek kontrolü, MATBQF adet-çap-aralık-boy eşleşmesi ve SO1 genel/tepe değer kontrolü yapılır. Noktasal SO1 As alanları henüz çözülemedi. Çizim altına bu sınırlama yazılır. PDF okunmaz veya gerekmez.

Girdi: aynı kökte ST4, RAD (V14.01), SO1, MATBQF ve `_MatTem.dwg`. Kaynak dosyalar salt okunur açılır. Bu kaynak düzeni ve sıfır asal açı N_ULK_02 üzerinde doğrulanmıştır; genel STA sürüm desteği iddiası yoktur. Dördüncü ilave STA çizimi/MATBQF'deki 392 cm boyuyla aktarılır; radson.pdf'deki 280 cm farkı çözülmedi ve otomatik dönüştürülmedi.

N_ULK_02 sonucu: 9ø12/33 L=365 alt; 7ø12/33 L=360 alt; 8ø12/33 L=370 alt; 4ø12/33 L=392 alt; 8ø12/33 L=410 üst. Son iki grup düşeydir. Bunlar kayıtlı seçimlerdir, sürekli ısı konturları değildir.

Doğrulama: `Build.ps1 -OutputName final` net48 0 hata/0 uyarı; `tools/Verify-Project.ps1` dört PASS. Core Console 2027/net10 native koşucusu, PDF bulunmayan fixture ile 1216 nesne, beş etiket, sonlu cm sınırları, kaynak uyarısı ve gerçek simge yükleyicisini doğruladı. Eksik SO1 ve uyumsuz MATBQF hiçbir hedef nesnesi eklenmeden reddedildi. Sonuç: `final-result.txt`. Koşucu sürümü teslimin 2027 uyumluluk onayı değildir. Canlı AutoCAD modal form/panel testi yapılmadı.

Teslim: `NETLOAD/ST4_Yardimci_20261020_171800.dll` ve PDB; kaynak/teslim DLL SHA256 aynı. Önceki 161449 çiftinin yedeği `eski-netload/` içinde. Kullanıcı AutoCAD oturumuna DLL otomatik yüklenmedi. Örnek: `radye-karsilastirma.dwg`; `radye-karsilastirma.preview.png` DXF üzerinden yaklaşık geometri önizlemesidir, AutoCAD plot değildir.

Monolit simge built-in imagegen ile üretildi. Son varlık `ST4_Yardimci/Icons/radye_otomatik_monolit.png`, kaynak `icon-source.png`. İstem `icon-prompt.txt` içinde; 28/32 px önizleme görsel olarak kontrol edildi.
