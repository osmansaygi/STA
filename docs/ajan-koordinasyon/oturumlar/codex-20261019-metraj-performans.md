# Donatı metraj performansı
- Ajan: Codex / bu sohbet
- Güncelleme: 2026-10-19 (Europe/Istanbul)
- Durum: tamamlandı
- Görev: DONATIMETRAJ yavaşlamasını mevcut hesap/seçim/balon davranışını koruyarak dar kapsamda düzelt.
- Ayrılan dosyalar: yok. Değişen: ST4_Yardimci/DonatiMetrajRunner.cs; docs/modul-durum/ST4_Yardimci.md; tools/MetrajPerf/**.
- Ortak çıktı kaynakları: yok; tools/MetrajPerf/build ve obj izole, NETLOAD/bundle/setup kopyaları devre dışı.
- Sonuç: tablo araması satır bazlı; doğrudan seçimde renkli balon çizim sırası toplu. 1000 pozda tablo ~2,9 s→0,2 s; renkli balon ~6,5 s→0,058 s. Hesap/seçim/görünüm aynı.
- Doğrulama: 1/31/32/33/1000 poz renkli-renksiz çıktı, kg, XData ve tam çizim sırası eşit; Verify-Project PASS; Debug/net48 0 hata/0 uyarı. İlk büyük Core Console doğrulaması CoreLib/0xc000001d ile sonlandı; değişmeyen DLL tekrarında COMPLETE. Öncesindeki eski DWG açılışında acdb26/0xc0000409; host sonlanmaları çözülmüş sayılmaz.
- DLL: tools/MetrajPerf/build/after/ST4_Yardimci_20261019_151739.dll. NETLOAD/bundle/setup kopyalanmadı. Canlı kullanıcı çizimi doğrulaması açık; otomatik DLL yüklenmedi.
