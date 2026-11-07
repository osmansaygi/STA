# Codex — Kiriş açılımı net donatı aralığı sorgusu
- Ajan: Codex / bu sohbet; tarih 28.10.2026 Europe/Istanbul.
- Durum: tamamlandı; ayrılan dosya/kaynak yok.
- Görev: mevcut kiriş açılımındaki net mesafe ayarını salt okunur belirleme.
- Kontrol: protokol/C# çalışma düzeni, ilgili kesit kodu ve kiriş referansı, modül ve güncel koordinasyon kayıtları, güncel NETLOAD kontrol edildi. Ortak kaynak/derleme/DLL değişmedi.
- Sonuç: KirisKesitSiraXleri satır 65 pitch=4.0+diameter → yatay net hedef 4 cm. KirisKesitCubukYerlesimi satır 141: sıra derinliği += önceki çap/2 + 3.0 + yeni çap/2 → düşey net 3 cm. maxDia cm'ye çevriliyor. Sığmayan donatı bir sonraki sıraya aktarılıyor.
- Doğrulama: kaynak okuması; test/derleme/DLL kopyalama/canlı AutoCAD yapılmadı. Kod değişikliği yok.
