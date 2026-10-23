# Claude Code — MERDIVEN detayında kiriş ölçü/genişlik hatası
- Ajan: Claude Code / yan oturum (KOLONDUSEY oturumundan)
- Güncelleme: 2026-10-23 20:24 (Europe/Istanbul)
- Durum: tamamlandı — rezervasyon yok
- Görev: MERDIVEN plan/kesit detayında kiriş yüksekliği/genişliği yanlış okunuyor/ölçülendiriliyor; neden + dar düzeltme.
- Değişen: ST4_Yardimci/MerdivenRunner.cs (TryBoundaryOnAxis kiriş dalı, TryKirisNearFace/TryKirisFarFace/MatchKirisLabel, KirisLabel, TryReadKirisLabel yazı merkezi+açı, sağ kiriş min 15); docs/modul-durum/ST4_Yardimci.md; NETLOAD/OKU.txt; NETLOAD/GUNCEL_2027/** (eski paket NETLOAD/arsiv/GUNCEL_2027_20261023_202154_917). Test: tools/MerdivenKiris20261023/** (izole build/obj, Native probe, Run.ps1).
- Doğrulama: Yardımcı net10/2027 izole 0/0 → ST4_Yardimci_2027_20261023_201748.dll. Core Console 2027 probe (ayrı Line ve kapalı polyline kiriş): eski 25 cm/50 varsayılan, yeni etiketlerle aynı 30/60, 35/70; COMPLETE exit=0. merdiven.ini test öncesi yedeklenip geri yüklendi. Genel Verify: pwsh yok, koşulmadı. Canlı AutoCAD/gerçek kullanıcı planı denenmedi.
- Açık: kullanıcının gerçek planında kontrol; kiriş etiketi okunmazsa genişlik geometriden, yükseklik form varsayılanından.
