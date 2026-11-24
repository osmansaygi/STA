# claude-20261123-merdiven-riht-baslangic

- Ajan: Claude (Code). Görev: Yardımcı MERDIVEN formuna "Rıht başlangıç no" seçeneği (plan 1/25 rıht numaraları 1'den başlamayabilir).
- Durum: tamamlandı (23.11.2026 13:10) — rezervasyon yok.
- Kontrol: protokol + son 2 gün kayıtları (21–23.11 kayıt yok, aktif çakışma yok). Başlangıçta GUNCEL_2027 = Plan 20261117_155353 + Yardımcı 20261116_171202.
- Değişen: ST4_Yardimci/MerdivenForm.cs (MerdivenSettings.RihtBaslangicNo + ini + form alanı), ST4_Yardimci/MerdivenRunner.cs (AnnotatePlan125RihtNos i + başlangıç), docs/modul-durum/ST4_Yardimci.md. Yeni test aracı tools/MerdivenRiht20261123.
- Derleme: proje %TEMP%\yrdriht\src'ye kopyalanıp Release/net10 AutoCAD2027Build, 0 hata (proje içi obj/bin ve NETLOAD'a dokunulmadı).
- Doğrulama: Core Console 2027 probe — başlangıç 1 → 1…12, 7 → 7…18, 0 → 1…12; ini satırı yazılıyor (kullanıcı ini'si geri yüklendi).
- Yayın: GUNCEL_2027 = Plan 20261117_155353 + Yardımcı 20261123_130542 (hash eşit), arşiv NETLOAD/arsiv/GUNCEL_2027_20261123_130649_426; eski Yardımcı DLL/PDB tools/netload-yedek/GUNCEL_2027_20261123. Not: AutoCAD açıkken klasör bütün değiştirilemedi (NetTopologySuite kilitli), dosya bazında güncellendi. Commit yok. Canlı AutoCAD denenmedi.
- Ek görev (23.11.2026 13:55, tamamlandı — rezervasyon yok): MERDIVEN B-B saf sahanlık kesiti düşey eksende ölçülüyordu (perde+konsol). ST4_Yardimci/MerdivenRunner.cs: `LandingAxis` + `MeasureLandingToSupportsCm(..., axis)`, plan sahanlık ölçüsü aynı eksende. Test RunSah.ps1: B-B 410 cm (360 + 2×25 perde), A/C değişmedi. Yayın GUNCEL_2027 = Yardımcı 20261123_135417 + Plan 20261117_155353 (hash eşit), arşiv NETLOAD/arsiv/GUNCEL_2027_20261123_135445_037; 130542 tools/netload-yedek/GUNCEL_2027_20261123'e taşındı. Commit yok, canlı AutoCAD yok.
