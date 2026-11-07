# Claude — Poligon ara başlık: kenara sarkan düşeyler eşit aralık + etiket adedi
- Ajan: Claude Code. Güncelleme: 28.10.2026 23:25 (Europe/Istanbul). Durum: tamamlandı — rezervasyon yok.
- Görev: kullanıcı ekranları — T orta saplanmada ara başlığın sarkan kısımlarında düşey aralıklar eşit değil; ara etiketi "14ø16" (20 demir); saplanan perde ölçüsü.
- Kontrol: Cursor kayıtları (23:01'e kadar) tamamlandı. Görev başı GUNCEL_2027 = Plan 230038 + Yardımcı 205437.
- Değişen: ST4_Plan_ID_Ciz/PlanIdDrawingManager.KolonDusey.cs (PlacePoligonAraBaslikMinDuseyDonati sabit istasyon parametresi; yeni PoligonAraBaslikIstasyonluKonumlar, PoligonAraBaslikSaplananIstasyonlar; FinalizePoligonAraBaslikDonati etBoxes; etiket UcAraBaslikaBagli), docs/modul-durum/ST4_Plan_ID_Ciz.md, NETLOAD/OKU.txt.
- Doğrulama: izole Release net10/2027 0 hata. Core Console (tools/KolonKirisEtriye20261028: KKE_ARA sentetik, KKE_DRAW + dökümler/Render.ps1 PNG): sentetik 640×30 T sarkan 15/15 cm; SAS_01 14ø16 → 20ø16, SAS_D1_03 S-59 eşit aralık + 16ø16; saplanan kol ölçüleri (270/60, 147/30/30/30/147) mevcut. HHO_01, N_ULK_02, PS_A_BL aralık yazıları/etriye sayısı birebir.
- Yayın: GUNCEL_2027 = Plan 20261028_232036 + Yardımcı 205437, hash PASS; eski paket arsiv/GUNCEL_2027_20261028_232338_906. Canlı AutoCAD denenmedi. Commit yok.
- Açık: kullanıcının 1. resmindeki yatay kanatlı (dikey saplanan) yönelim gerçek projede bulunamadı; sentetik kesitte doğrulandı, etiket/ölçü o yönde canlı kontrol bekliyor.
