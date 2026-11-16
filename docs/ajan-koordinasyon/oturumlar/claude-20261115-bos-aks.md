# claude-20261115-bos-aks

- Ajan: Claude (Claude Code)
- Görev: Tüm Plan ID planlarında (kalıp, ST4PLANID, temel, mantar, kolon aplikasyon) kat küçülünce boşa çıkan aksları çizmemek; aks o kattaki bir kolonun (perde dahil) içinden ya da kenarından geçiyorsa kalır.
- Güncelleme: 15.11.2026 15:22 (Europe/Istanbul)
- Durum: tamamlandı — rezervasyon yok
- Kontrol: protokol + son 2 gün kayıtları (aktif yok; eski "aktif" kayıtlar 3+ hafta bayat). Görev başı GUNCEL_2027 = Plan 20261114_162729 + Yardımcı 20261115_150529; o Plan yayınından beri Plan kaynağında yalnız bu değişiklik.
- Değişen: ST4_Plan_ID_Ciz/PlanIdDrawingManager.cs (DrawAxes'e kat parametresi, 5 çağrı yeri; yeni BuildFloorColumnCrossingAxisIds, tolerans 0,5 cm), docs/modul-durum/ST4_Plan_ID_Ciz.md, tools/AksBosKat20261115/** (Build.ps1, Run.ps1, Publish51.ps1, Native probe AK_DRAW).
- Doğrulama: izole Plan 2027 derlemesi 0 hata. Core Console CK_20827_2_02 eski/yeni DLL: ST4PLANID 195→135, KALIP50 312→192, temel 39→39 aks; kalan aksların hepsi KOLON/PERDE çizimine ≤0,5 cm, kalkanların hiçbiri değmiyor.
- Yayın: GUNCEL_2027 = Plan 20261115_151724 + Yardımcı 20261115_150529 (PASS), arşiv NETLOAD/arsiv/GUNCEL_2027_20261115_152036_192. Commit yok. Canlı AutoCAD denenmedi.
- Not: aks sınırı zarfı (aks uç boyu, pafta yerleşimi) ve kesit hattı balon uzantısı değiştirilmedi.
- Ek görev (15.11.2026 15:43, tamamlandı — rezervasyon yok): kullanıcı ekranı: aks silinince balon/ölçü uzakta kaldı. `GetAksSiniriEnvelope(union, floor)` kat süzgeciyle; plan çağrılarına kat verildi (PlanIdDrawingManager.cs, PlanIdDrawingManager.Sections.cs 1960/1969). Core Console CK_02: küçülen katlarda taşma 800/900 → 280 cm, en üst kat 4495 → 215 cm; alt katlar aynı. Yayın GUNCEL_2027 = Plan 20261115_154042 + Yardımcı 20261115_150529 (PASS), arşiv NETLOAD/arsiv/GUNCEL_2027_20261115_154213_538. Canlı AutoCAD yok, commit yok.
