# Aktif modüller (kısa indeks)

| Modül | Durum dosyası | Ana komut / klasör | Son not |
|-------|---------------|--------------------|---------|
| ST4 Yardımcı | [ST4_Yardimci.md](ST4_Yardimci.md) | `KURANGLEZ` / `HASIRDONATI` / `KAPAK` | Kuranglez + hasır donatı; 24.11 KAPAK (statik proje kapağı) |
| Plan ID çiz | [ST4_Plan_ID_Ciz.md](ST4_Plan_ID_Ciz.md) | `KALIP50` / `KOLONDUSEY` | [Sürekli temel beton görünüşü](ST4_Plan_ID_Ciz.SurekliGorunus.md) |
| Statik rapor (Claude) | [StatikRaporClaude.md](StatikRaporClaude.md) | `StatikRaporClaude/` masaüstü | 0.8: analiz dosyalarından GPR'siz rapor (FEA3D, radye, özel tablolar, metraj, hesap çıktıları) |
| GES çatı raporu | [GesRaporu.md](GesRaporu.md) | `GesRaporu/` masaüstü "GES Hesap Raporu" | 1.0: mimari DWG kapak/kot/çatı planı → PDF (kar, rüzgar, kaplama, bulon, vida, profil) |
| PDF rapor birleştirme | [RaporBirlestir.md](RaporBirlestir.md) | `RaporBirlestir/` masaüstü "Rapor Birleştir" | 1.4: statik / iskele / kalıp iskele tek sayfada (iskele listesi kalıcı); kapak + eklerle birleştirip MS_B_RPR / _ISK_RPR / _KAL_RPR adlarıyla kaydeder; STA4CAD kolon paspayını 4 / 5,5 yapar; yeni kapakta eski kapağı çıkarır |

Yeni modül eklenince bu tabloya bir satır + aynı isimde `.md` ekle.

Yardımcı ve Plan ortak form görünümü: [Monolit + AutoCAD açık/koyu teması](ST4_FormTasarimi.md).

Plan ID çökme takibi ve sürüme göre DLL dağıtımı: [23 Ekim stabilite kaydı](ST4_Plan_ID_Ciz.Stabilite.md).

Genel temizlik, arşivden geri alma ve çizim dışı stabilite: [23 Ekim bakım kaydı](ST4_Bakim.md).

STA4CAD dosya sözlüğü (ST4, GPR, KMX, kiriş dosyaları, sehim tablosu): [../sta4cad-statik-dosyalari.md](../sta4cad-statik-dosyalari.md). Örnek klasör `adem_tafli\STATIK` (A_TFL). Sehim tablosu referansı VKF bodrum `VKF_01_ka1.dwg`.

Kiriş donatı çizimi referansı (sınır değerler, kenetlenme, açılım sırası): [../kiris-donati-cizim-referansi.md](../kiris-donati-cizim-referansi.md).
