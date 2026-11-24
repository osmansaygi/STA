# claude-20261124-rapor-birlestir

- Ajan: Claude (Code). Görev: Statik / İskele / Kalıp iskele raporlarını kapak + rapor + eklerle birleştiren bağımsız PDF birleştirme programı; masaüstü kısayolu ve simge.
- Durum: tamamlandı (24.11.2026 18:40) — rezervasyon yok.
- Kontrol: protokol + son 2 gün kayıtları (beykent-kapak, ges-kapak-okuma, selcuklu-kapak: tamamlandı; aktif yok). AutoCAD DLL/NETLOAD/GUNCEL_2027'ye dokunulmadı (bağımsız exe).
- Değişen: yeni `RaporBirlestir/` (csproj, Program, AnaForm, Birlestirici, Varliklar/SimgeUret.ps1 + birlestir.ico/png), `docs/modul-durum/RaporBirlestir.md`, `docs/modul-durum/INDEX.md` (1 satır).
- Çıktı: `RaporBirlestir/bin/Uygulama/RaporBirlestir.exe`; masaüstü "Rapor Birleştir.lnk".
- Doğrulama: Release 0 hata/0 uyarı. `--test` 3 bölüm/5 parça → 6 sayfa, yeniden okumada yer imleri doğru sayfada; hatalı aralıkta çıktı yazılmadı. `--ekran` arayüz görüntüsü kontrol edildi. Commit yok.
- Açık: gerçek büyük STA4CAD raporu ve şifreli PDF ile denenmedi.
- Ek 1.1: kolon paspayı düzeltmesi (`RaporBirlestir/PaspayDuzelt.cs`, `Birlestirici.cs`, `AnaForm.cs`, `Program.cs`). Yalnız KOLON/PERDE bloğu; kiriş/temel dokunulmaz. 4 gerçek STA4CAD raporu (2 üretici, 1 elle düzenlenmiş) ile bozma/geri düzeltme + metin karşılaştırması + görsel kontrol. Release 0 hata/0 uyarı, bin/Uygulama güncel.
- Ek 1.2: tek sayfa arayüz, klasöre statik adından türetilen adlarla kayıt (_ISK_RPR / _KAL_RPR), üzerine yazma sorusu, bölüm başına kalıcı klasör. `RaporBirlestir/AnaForm.cs` yeniden yazıldı, `Birlestirici.cs` kaynakları bellekten açıyor.
- Ek 1.3: iskele listesi kalıcı (liste-iskele.txt, kullanıcının İskele_RPR.pdf'i ile başlatıldı); kayıt klasörü/adlar yalnız statikten.
- Ek 1.4: yeni kapak eklenince eski kapak atlanır (KapakAlgila.cs, PdfPig); 500 raporda algılama denetimi, 4 uçtan uca senaryo.
