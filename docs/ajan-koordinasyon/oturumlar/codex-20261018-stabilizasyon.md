# Codex stabilizasyon taraması

- Ajan: Codex / genel stabilizasyon
- Tarih: 2026-10-18 (Europe/Istanbul)
- Durum: tamamlandı
- Görev: Cursor değişikliklerini koruyarak nesne ömrü ve çökme riskleri taraması.
- Ayrılan dosyalar: yok (aşağıdaki kapsam kayıtları tamamlanan işe aittir).
- Ortak çıktı kaynakları: yok (derlemeler tamamlandı; aşağıdaki kaynaklar serbest).
- Bulgu: geçici native geometri nesnelerinde Dispose eksikleri; offset adaylarında ve patlatma hata yolunda sahiplik temizliği eksikleri.
- Kapsam ekleme: ST4_Plan_ID_Ciz/CommandEntry.cs (Idle içinde doğrudan panel yaratmayı Yardımcı ile aynı komut kuyruğu düzenine taşıma).
- Ortak çıktı kaynakları: ST4_Yardimci/bin, ST4_Yardimci/obj, ST4_Plan_ID_Ciz/bin, ST4_Plan_ID_Ciz/obj, NETLOAD, ST4_Yardimci/Autoload/ST4_Yardimci.bundle/Contents/Windows, ST4_Yardimci/Setup-Ofis/payload. Derlemeler sırayla.
- Kapsam ekleme: ST4_Yardimci/DosemeDonatiRunner.cs (üç geçici probe nesnesinin ömrü), tools/Test-StaStartup.ps1 (başlangıç regresyon testi).
- Sonuç: 7 kaynak dosyasında dar kapsamlı nesne ömrü/başlangıç/girdi kontrolü düzeltmeleri; Cursor'un mevcut geometri ve çizim değişiklikleri korundu. Yeni komut veya ikon eklenmedi.
- Doğrulama: iki Debug/net48 derlemesi başarılı. Yardımcı 0 hata/0 uyarı; Plan 0 hata, NU1900 güvenlik veri kaynağı erişim uyarısı. Test-YardimciStartup, Test-StaStartup, Test-BaDimensionStyles, Test-TemelDetayGeometry başarılı. Değiştirilen kaynaklarda git diff --check geçti.
- DLL: NETLOAD/ST4_Yardimci_20261018_144420.dll; NETLOAD/ST4_Plan_ID_Ciz_20261018_144329.dll. Build hedefleri Yardımcı bundle ve setup payload çıktısını da güncelledi.
- Açık doğrulama: gerçek AutoCAD'de panel açılışı, OTOOLCU/DONATI OKU/DOSEMEDONATI, ISKELEPLAN/ISKELEKESIT ve ardından MOVE/COPY; uzun oturum bellek davranışı. Bu çalışmada AutoCAD'e DLL yüklenmedi; net48 çıktıları AutoCAD 2027 uyumluluğu doğrulamaz.
