# Kiriş açılımı — boşluk ve paspayı

28.10.2026, AutoCAD 2027 / net10.0-windows.

KIRISACILIMLARI formu: yatay net boşluk 4 cm, düşey sıra net boşluğu 3 cm, etriye dışı paspayı 4 cm. Değerler aynı AutoCAD oturumunda hatırlanır. Yeni açılımın kesit ve görünüşü bu değerleri kullanır; mevcut çizimler değiştirilmez. Boyuna donatı merkezinin eski şematik farkları korunur (görünüş örtü+1.5, kesit örtü+1). Sürekli temel eski değerlerini kullanır.

Kaynak kapsamı: KirisAcilimForm.cs, PlanIdDrawingManager.KirisAcilim.cs, PlanIdDrawingManager.KirisAcilim.Kesit.cs. Alanlar seçenek nesnesiyle çizim yöneticisine taşınır. Kiriş yöneticisine özel durum; değişen paspayı kullanan özel yardımcılar instance yöntemine çevrildi. Dar kesitte geçersiz paspayı çizim başlamadan reddedilir.

Doğrulama: Release 0 hata / 3 mevcut uyarı. Native/Probe.cs gerçek Core Console 2027 içinde form/yerleşim testini ve kopya CK_20827_2_01 çizimini çalıştırır. Run.ps1 ayrı süreç kullanır. 10 çubukla yatay 4 ve 6, düşey 3 ve 5, paspayı 4 ve 6; 15 cm büyük yatay boşlukta aralık korunması PASS. Form 4/3/4 ve Çiz ile aktarım PASS.

Eski DLL ile Release varsayılan kiriş parmak izi: 35334 nesne / 0E34009B8A5663C8014F4B42005232AF28B1088D17B1FDD3E4C516FEC75282BE, birebir.
Eski DLL ile özel kiriş seçeneklerinin verildiği sürekli temel parmak izi: 19356 nesne / 54BCB58DEC22217A949B204A876ED81307133680E9F350C5982A3EF6E4F8F1DB, birebir.
Özel 6/5/6 kiriş çizimi: 35316 nesne / 9986A3B2346CD602CB4654452874AB18D0F491E7985F00FAA7265B7E467F8C8A.

Genel Verify-Project, önceden mevcut Test-StaStartup.ps1 fixture'ında PlanLisans eksikliği nedeniyle durdu. Üretim lisans kodu/test fixture'ı bu görevde değiştirilmedi.

Yayın: Publish-CurrentNetload2027.ps1 ile NETLOAD/GUNCEL_2027/ST4_Plan_ID_Ciz_2027_20261028_191914.dll; Yardımcı205437 korundu. SHA256 ve iki net10 modül PASS. Çalışan AutoCAD'e DLL yüklenmedi; kullanıcı canlı form/çizim doğrulaması yapılmadı.
