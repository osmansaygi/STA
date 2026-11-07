# Claude — KALIP DENEME 2 (DENEME2)
- Ajan: Claude Code
- Güncelleme: 24.10.2026 15:30 (Europe/Istanbul)
- Durum: tamamlandı — rezervasyon yok
- Görev: DENEME2 komutu — KALIP50ST4 (donatılı/GPR yazılı) kalıp planının donatılı kopyasına DENEME1 EKSEN X/Y artılarını çizer; BEYKENT ribbon Deneme grubuna düğme.
- Değişen: ST4_Plan_ID_Ciz/CommandEntry.cs (DENEME2); ST4_Plan_ID_Ciz/PlanIdDrawingManager.cs (Kalip50LayoutFlags.DrawDeneme1EksenOnDonatiCopy, DrawFormworkPlan50 döngüsü + özet satırı, DrawSlabs toplama, DrawDeneme2DosemeEksenArtilari); ST4_Plan_ID_Ciz/BeykentRibbonCatalog.cs; ST4_Plan_ID_Ciz/CommandPaletteManager.cs; ST4_Plan_ID_Ciz/StaMonolitIcons.cs; ST4_Plan_ID_Ciz/Icons/kalip_deneme2_monolit.png (yeni); tools/PlanRibbonCheck/Probe/Program.cs (21 komut); tools/KalipDeneme2_20261024/** (yeni probe); docs/modul-durum/ST4_Plan_ID_Ciz.md, ST4_Plan_ID_Ciz.Ribbon.md.
- Doğrulama: izole net10/2027 derleme (tools/PlanRibbonCheck/build/ST4_Plan_ID_Ciz_2027_20261024_151248.dll) 0 hata/4 bilinen uyarı. Ribbon regresyonu PASS. Core Console CK: KALIP50 vs DENEME2 fark yalnız 140+140 EKSEN çizgisi; ilk KALIP50 koşusu c0000409 (bilinen makine kararsızlığı), tekrar COMPLETE. Canlı AutoCAD denenmedi.
- Açık: artı GPR yazı kesişimine biniyor.
## Devam — çökme + yayın (24.10.2026 15:20)
- Kullanıcı: AutoCAD çöktü, BEYKENT'te düğme yok. Döküm acad.exe.24256.dmp (15:17): yüklü Plan DLL eski GUNCEL_2027 145505 (DENEME2 içermiyor → düğme yok). Hata clrjit.dll c0000005; aynı saatlerde csc.exe AccessViolation, accoreconsole clrjit c0000409, açılışta WHEA 17 (14:43) → değişiklikten bağımsız, makine kararsızlığı.
- Yayın (PS5.1 uyarlaması, hash PASS): NETLOAD/GUNCEL_2027 = Plan 20261024_151248 + Yardımcı 213059; eski paket NETLOAD/arsiv/GUNCEL_2027_20261024_151852_778; NETLOAD/OKU.txt sona satır. Canlı AutoCAD denenmedi.
## Devam 2 — ortak kontrol kuralı (24.10.2026 15:35)
- Kullanıcı: "Codex, Cursor ve sen her zaman kontrol etsin" (son kayıtlar, menü düzeni, otomatik yükleme).
- Ayrılanlar: docs/ajan-koordinasyon/PROTOKOL.md (yeni "Her görevde zorunlu kontrol" bölümü); AGENTS.md (tek madde); .cursor/rules/ajan-koordinasyon.mdc (tek madde); bu kayıt.
- Sonuç: kural PROTOKOL/AGENTS/.cursor kuralına eklendi (BaHomeRibbonCatalog.cs ve tools/GuncelOtoYukle2027 varlığı doğrulandı). Durum: tamamlandı — rezervasyon yok.
