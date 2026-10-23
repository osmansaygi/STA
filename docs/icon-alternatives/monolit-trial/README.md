# Monolit denemesi

20 komutluk simge seti ortak `BaCommandIcons.Load` yükleyicisine eklendi. Çizim komutları değiştirilmedi. Panelin sağ tık menüsünde **Simge tasarımı → Monolit / Klasik** ile anında geçiş yapılır. Seçim `icons.ini` içinde saklanır; kişisel `%LocalAppData%/ST4Icons/KOMUT.png` simgeleri önceki önceliğini korur.

Yeni DLL için AutoCAD'i yeniden başlatıp uygun NETLOAD dosyasını yükleyin. Zaten yüklenmiş eski bir DLL aynı oturumda değiştirilemez.

`panel-preview.png` uygulamanın gerçek simge yükleyicisinden alınan 28/32/64 px karşılaştırmasıdır; canlı AutoCAD ekran görüntüsü değildir. Testte 20 ayrı Monolit simgesinin ve 20 klasik yedek simgenin yüklenmesi, boyutlar ve alfa saydamlığı doğrulandı. İki hedefin derleme günlükleri `tools/MonolitSmoke/` altındadır.

Kaynak: `ST4_Yardimci/Icons/monolit_atlas.png`. Mevcut özgün simge dosyaları korunmuştur. Atlas tek yerleşik image_gen çağrısıyla üretildi; minimalist görseller yerel vektörel çizim koduyla hazırlandı.
