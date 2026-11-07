# Claude — MIMARIKAT merdiven: sınır (DOSEME SINIRI) + kesik rıhtlar
- Ajan: Claude Code
- Güncelleme: 30.10.2026 23:47 (Europe/Istanbul)
- Durum: tamamlandı
- Görev: Kullanıcı: ilk/son rıht ve merdiven sınırları alınmıyor; mimaride kesilen merdivende bazı rıhtlar alınmıyor. Mimari modunda merdiven kontur + göz DOSEME SINIRI'na; kısa/parçalı rıhtlar kola katılıp tam boy çizilir.
- Ayrılanlar: ST4_Yardimci/MimariMerdivenAnaliz.cs, ST4_Yardimci/MimariMerdivenRunner.cs, docs/modul-durum/ST4_Yardimci.md. Yayında NETLOAD/GUNCEL_2027/**, NETLOAD/arsiv, NETLOAD/OKU.txt.
- Sonuç: GUNCEL_2027 = ST4_Yardimci_2027_20261030_231350.dll (Publish51 PASS, arşiv GUNCEL_2027_20261030_231636_249). Merdiven plak sınırı+göz DOSEME SINIRI, son kenar paralel çizgi kontrolü, kesik rıht, dış merdiven ayırma. ERDENİZ/BLG Core Console + harness (değişmedi). Canlı AutoCAD yok. Açık: kesikli merdiven kuralı kullanıcıdan.
- 23:46 ikinci tur (kullanıcı: hâlâ düzgün değil): GUNCEL_2027 = ST4_Yardimci_2027_20261030_234141.dll (Publish51 PASS, arşiv GUNCEL_2027_20261030_234630_230). DOSEME SINIRI = kol uç çizgileri + göz (9 proje ölçümüyle). Araştırma notu docs/mimari-cizim-kurallari.md.
