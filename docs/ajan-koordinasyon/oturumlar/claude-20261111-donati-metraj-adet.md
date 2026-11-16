# claude-20261111-donati-metraj-adet

- Ajan: Claude (Claude Code)
- Görev: DONATIMETRAJ formuna adet çarpanı; metraj çarpanla alınır, tablo altına "BENZER N ADET" notu.
- Güncelleme: 11.11.2026 22:21
- Durum: tamamlandı — rezervasyon yok
- Kontrol: protokol + son 2 gün kayıtları; DonatiMetraj dosyalarını ayıran aktif iş yok. Görev başı GUNCEL_2027 = Plan 20261110_205853 + Yardımcı 20261111_183604.
- Değişen: ST4_Yardimci/DonatiMetrajForm.cs (AdetCarpan ayarı, ini'ye yazılmaz; form kutusu), ST4_Yardimci/DonatiMetrajRunner.cs (Poz.Benzer, BenzerCarpan, CarpHasir, PozlarFromHits benzer parametresi, YazKatNotu benzer notu), docs/modul-durum/ST4_Yardimci.md.
- Doğrulama: tools/OlcuStil20261110/Build.ps1 Yardimci 2027 → 0/0. Core Console ve canlı AutoCAD denenmedi.
- Yayın: GUNCEL_2027 = Plan 20261110_205853 + ST4_Yardimci_2027_20261111_222037.dll, arşiv NETLOAD/arsiv/GUNCEL_2027_20261111_222053_046. Commit yok.
- Açık: yalnız hasır tablosu çizildiğinde "BENZER" notu yazılmaz (adet/kg çarpılır).
