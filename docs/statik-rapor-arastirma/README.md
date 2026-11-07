# STA4 GPR'siz üretim araştırması

STA4 GPR çıktısındaki bölümlerin analiz dosyalarından nasıl üretildiği (kayıt yerleşimleri, ofsetler, formüller, sayı biçimi, doğrulama oranları). 25.10.2026, Claude Code alt ajanları; D:\projeler altındaki GPR'ler salt okunarak karşılaştırıldı.

| Dosya | Kapsam | Programdaki karşılığı |
|---|---|---|
| 00-ortak-bicim.md | ortak sayı biçimi, dosya listesi | Uretec/Proj.cs (Fmt) |
| genel-bolumler.md | giriş, yük analizi, opsiyon, program, kombinasyon, aks, deprem, mod, deplasman, devrilme | Uretec/*.cs |
| radye.md | radye (RAD, DE1, MATBQF) | Veri/RadyeTablolari.cs |
| hesap-ciktilari.md (+ hesap-burkulma, hesap-kiris-kesme, hesap-sehim, hesap-temel) | duvar yükleri, döşeme şerit/bilgileri, kiriş bilgileri, sehim, burkulma, kesme, temel yükleri, perde-diyafram | Uretec/HesapTablolari.cs |
| metraj-ozel-fea3d.md, metraj-doseme-donati.md | metraj | Uretec/Metraj.cs |
| ozel-tablolar.md | süneklik, temel sünek tesirleri, dengeli donatı, imar, yorum | Uretec/OzelTablolar.cs |
| fea3d.md | Fea3D/FeaMod/FeaVec yerleşimi ve tüm FEA3D tabloları | Uretec/Fea3D.cs |

Raporlardaki `scratchpad\ara_*` araç yolları geçiciydi ve silinmiş olabilir. Devir notu: `docs/statik-rapor-claude-CURSOR-DEVIR.md`.
