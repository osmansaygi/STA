# Claude — Kolon düşey: "Tüm kolon sıklaştırılsın" seçeneği
- Ajan: Claude Code
- Güncelleme: 30.10.2026 13:55 (Europe/Istanbul)
- Durum: tamamlandı — rezervasyon yok
- Görev: KOLONDUSEY / KOLON aplikasyon formlarındaki etriye aralığı seçimine üçüncü seçenek: kolon boyunca sıklaştırma aralığı.
- Kontrol: son 2 gün kayıtları okundu; aktif çakışma yok (Codex form-hafıza kaynakları serbest). Görev başı GUNCEL_2027 = Plan 20261028_232036 + Yardımcı 20261029_163542.
- Değişen: ST4_Plan_ID_Ciz/KolonDuseyForm.cs (KolonDuseySettings.TumKolonSiklastir + ini; combo 3 seçenek: Normal aralık / Kiriş bölgesi sıklaştırılsın / Tüm kolon sıklaştırılsın; bölüm adı «Kolon etriye sıklaştırma»), ST4_Plan_ID_Ciz/KolonApplicationForm.cs (bölüm adı), ST4_Plan_ID_Ciz/PlanIdDrawingManager.KolonDusey.cs (DrawKolonGorunusEtriyeler: tüm kolonda tekAralik, sMid = sLb = sSik), tools/KolonKirisEtriye20261028/Native/Probe.cs (KD_SIK=2), docs/modul-durum/ST4_Plan_ID_Ciz.md, NETLOAD/OKU.txt.
- Doğrulama: izole Release net10/2027 0 hata/4 bilinen uyarı (20261030_135143). Core Console 2027 CK_20827_2_01 KOLONDUSEY t0/t1/t2 COMPLETE: t0/t1 aynı eski davranış; t2'de üç bölgeli kolonlar tek etiket (ör. 12ø10/8+12ø10/10+12ø10/8 → 39ø10/8). Formlar elle açılmadı.
- Yayın: GUNCEL_2027 = Plan 20261030_135143 + Yardımcı 20261029_163542, Publish51 hash PASS; eski paket NETLOAD/arsiv/GUNCEL_2027_20261030_135248_375. Canlı AutoCAD denenmedi. Commit yok.
