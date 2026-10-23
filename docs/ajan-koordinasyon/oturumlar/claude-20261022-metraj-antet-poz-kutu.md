# claude — DONATIMETRAJ antet yolu poz balonu tablo kutusuna ortalama

- Ajan: Claude (yan oturum)
- Güncelleme: 2026-10-22
- Durum: tamamlandı (ayırma yok)
- Görev: Antet seç yolunda (`RunByAntet` → `StampLabelsFast`) tablolu donatı yazılarının poz balonu/numarası, normal yoldaki gibi sol poz sütunu hücresine yatay+düşey ortalansın.
- Değişen: ST4_Yardimci/DonatiMetrajRunner.cs (`StampLabelsFast` cells parametresi, `SelectIcAntetIdsInFrame`, RunByAntet çağrısı); ST4_Yardimci/BaMetrajPozCell.cs (`Collect(tr, ids)`, `AddSegments`, `Fit`; `Center` aynı matematik Fit üzerinden); docs/modul-durum/ST4_Yardimci.md; NETLOAD/OKU.txt.
- Doğrulama: scratchpad izole derleme (yalnız ST4_Yardimci, Debug/net48) 0 hata/0 uyarı. Teslim NETLOAD/ST4_Yardimci_20261022_181324.dll + PDB, SHA256 kaynak=kopya. AutoCAD canlı deneme yok. Hasır hızlı yolu değişmedi.
