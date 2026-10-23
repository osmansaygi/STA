# Claude — Oto Donatı X/Y çap + sehpa detayı

- Ajan: Claude / bu sohbet
- Güncelleme: 2026-10-22 18:07 (Europe/Istanbul)
- Durum: tamamlandı — rezervasyon yok
- Görev: OTODONATI formunda üst/alt çap-aralık X ve Y ayrı (manuel yalnız X); Sehpa tikliyse sehpa Ø kutusu aktif ve temel antetinin üstüne (alan + ana temel kotu otomatik) sehpa detayı.
- Değişen: ST4_Yardimci/OtoDonatiForm.cs; ST4_Yardimci/OtoDonatiSettings.cs; ST4_Yardimci/OtoDonatiRunner.cs; ST4_Yardimci/OtoDonatiSehpa.cs (yeni); ST4_Yardimci/SehpaDonatiRunner.cs (InsertBottomLeft + alttan hizalama seçeneği); docs/modul-durum/ST4_Yardimci.md; NETLOAD/OKU.txt.
- Derleme: izole Debug/net48 (scratchpad çıktısı, ana bin/obj kullanılmadı) 0 hata/0 uyarı.
- DLL: NETLOAD/ST4_Yardimci_20261022_180638.dll (önceki 175743 de klasörde) + pdb (SHA256 kaynak=kopya). Plan ID değişmedi.
- Test: otomatik test yok (pwsh yok; Verify-Project çalıştırılmadı). AutoCAD içinde denenmedi.
- Açık: canlı AutoCAD'de form düzeni, X/Y çizimi, sehpa kot okuma ve antet üstü konumu kontrolü.
- Düzeltme 18:06: sehpa alanı boşluk düşülmeden; kot RD-.. d=.. etiketinin altındaki çiftin üstü (d farkıyla doğrulanır), etiket yoksa formdaki üst kot.
