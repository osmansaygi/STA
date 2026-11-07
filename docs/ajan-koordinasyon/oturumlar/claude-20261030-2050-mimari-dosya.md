# Claude — MIMARIBOSLUK/MIMARIMERDIVEN: mimariyi ayrı DWG'den okuma + şeritte Mimari grubu
- Ajan: Claude Code
- Güncelleme: 30.10.2026 21:04 (Europe/Istanbul)
- Durum: tamamlandı — rezervasyon yok
- Görev: Kullanıcı mimari ve statik kalıbı aynı dosyaya koyamıyor; komutlar mimari DWG'yi diskten okuyup kat planını seçtiriyor; iki komut Home şeridinde ayrı "Mimari" grubunda.
- Değişen: ST4_Yardimci/MimariBoslukRunner.cs (HizaliOku: kalıp penceresi → Dosya/Cizim, yan veritabanı okuma, MimariDosyaSor, KatSec, PlanBasligi, blok adı kaydı), ST4_Yardimci/MimariBoslukAnaliz.cs (Hizalama.Adaylar, Kaynak.Basliklar/Bloklar), ST4_Yardimci/MimariMerdivenRunner.cs (betik bayrağı), ST4_Yardimci/BaHomeRibbonCatalog.cs (MIMARI grubu), tools/HomeRibbonCheck/Probe/Program.cs (6 panel, grup sırası), docs/modul-durum/ST4_Yardimci.md, NETLOAD/OKU.txt.
- Doğrulama: izole Release net10/2027 0/0 (20261030_210142). Core Console 6 koşu (2 komut × BLG/CK/NU), mimari yalnız ayrı dosyadan: sonuçlar önceki testlerle aynı; kat listesi CK'de blok adlarıyla. Test-HomeRibbon PASS.
- Yayın: GUNCEL_2027 = Yardımcı 20261030_210142 + Plan 20261030_184447, Publish51 hash PASS, eski arsiv/GUNCEL_2027_20261030_210207_886. Canlı AutoCAD denenmedi. Commit yok.
