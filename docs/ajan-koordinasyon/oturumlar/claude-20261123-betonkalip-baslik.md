# claude-20261123-betonkalip-baslik

- Ajan: Claude (Code). Görev: Yardımcı BETONKALIP tablosu — başlıkta proje adı yazılmasın; sütun başlığı "BETON-C25 (m³)" (ST4 /Materials/ fck/10).
- Durum: tamamlandı (23.11.2026 16:19) — rezervasyon yok
- Kontrol: protokol + son 2 gün kayıtları (merdiven-riht tamamlandı, aktif yok). GUNCEL_2027 = Plan 20261117_155353 + Yardımcı 20261123_135417.
- Düzenlenecek: ST4_Yardimci/BetonKalipRunner.cs, ST4_Yardimci/BetonKalipVeri.cs, docs/modul-durum/ST4_Yardimci.md.
- Ortak çıktı: izole Yardımcı 2027 derlemesi (%TEMP%), NETLOAD/GUNCEL_2027 (yalnız Yardımcı DLL).
- Değişiklik: `BetonKalipProjesi.BetonSinifi` (/Materials/ fck ≥ 100 → "C" + fck/10); başlık "BETON / KALIP METRAJI" (proje adı kaldırıldı); sütun "BETON-C30 (m³)", sınıf okunamazsa "BETON (m³)".
- Derleme: %TEMP%\yrdbk\src izole kopya, Release AutoCAD2027Build 0 hata/0 uyarı.
- Doğrulama: Core Console tools/BetonKalip20261026/Run.ps1 — BODRUM.ST4 (300) → BETON-C30, CAMI.ST4 (350) → BETON-C35; başlıkta proje adı yok (cins_bodrum.png). Canlı AutoCAD yok.
- Yayın: GUNCEL_2027 = Plan 20261117_155353 + Yardımcı 20261123_161721 (hash eşit), arşiv NETLOAD/arsiv/GUNCEL_2027_20261123_161754_599; 135417 tools/netload-yedek/GUNCEL_2027_20261123'e taşındı. Commit yok.
