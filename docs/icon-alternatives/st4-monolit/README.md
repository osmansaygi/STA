# ST4 ID — Monolit

Komutlar ve Kiriş düzelt sekmelerindeki 24 komut için Monolit simgeleri. Sıcak beton, kobalt mavi ve turuncu vurgular Yardımcı paneliyle aynı görsel aileyi kullanır.

AutoCAD yeniden başlatıldıktan sonra son ST4_Plan_ID_Ciz DLL'sini NETLOAD ile yükleyin. Paneli STAPANEL açar. Sağ tık → Simge tasarımı → Monolit / Klasik ile iki sekme de anında güncellenir. Kullanıcıya özel ST4Icons PNG dosyaları önceliğini korur. Klasik kaynak dosyaları korunmuştur. Seçim LocalAppData/ST4_Plan_ID_Ciz/icons.ini içine kaydedilir.

Bu çalışmada çizim üretme, donatı hesaplama ve komut çalıştırma mantığı değiştirilmedi. Sadece simge kaynağı, sunum yükleyicisi ve tasarım menüsü eklendi.

panel-preview.png gerçek simge yükleyicisinin 32/64 piksel çıktısıdır; canlı AutoCAD ekran görüntüsü değildir. tools/StaMonolitSmoke tüm simgelerin farklı olduğunu, 32x32 alfa saydamlığını, 24 klasik yedeği, düşey açılım takma adlarını ve iki sekmenin ortak yenilenmesini doğrular. Bu test kaydedilmiş kullanıcı ayarlarını değiştirmez veya çizim komutu çalıştırmaz.

Görsel kaynak tek yerleşik image_gen çağrısıyla üretildi. Orijinal atlas ST4_Plan_ID_Ciz/Icons/sta_monolit_atlas.png içinde gömülü kaynak olarak tutulur; kırpma ve ölçekleme simge yüklenirken yapılır.
