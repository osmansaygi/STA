# StatikRaporClaude — Cursor'a devir notu (25.10.2026, sürüm 0.8)

Claude Code oturumundan devralınan iş. Kurallar: `.cursor/rules/statik-rapor-claude.mdc`. Sürüm geçmişi ve doğrulama ayrıntısı: `docs/modul-durum/StatikRaporClaude.md`. Oturum kaydı: `docs/ajan-koordinasyon/oturumlar/claude-20261025-1500-statik-rapor.md`.

## Ne yapıyor
STA4CAD projesinden (ST4 + analiz sonuç dosyaları, varsa GPR) BEYKENT tasarımlı statik hesap raporu (PDF) üretir. WinForms, `net10.0-windows`, PDFsharp 6.2.0.
- GPR varsa: GPR sayfaları ayrıştırılıp modern tasarımla yeniden dizilir; GPR'de olmayan ama dosyadan üretilebilen bölümler eklenir.
- GPR yoksa: analiz tamamlanmış projede rapor yalnız sonuç dosyalarından üretilir (kullanıcının ana isteği: "analizi tamamlanmış projelerin GPR'siz çıktısı").

## Derleme ve teslim
```
cd StatikRaporClaude
dotnet build -c Release
```
- Release derlemesi otomatik olarak `StatikRaporClaude/bin/Uygulama/`'ya kopyalanır (csproj `KisayolKlasorunuGuncelle`). Masaüstü kısayolu `bin\Uygulama\StatikRaporClaude.exe`'yi açar. Uygulama açıksa kopya atlanır (uyarı) → kapatıp tekrar derle.
- Pencere başlığı: "Statik Rapor — Claude sürümü 0.8 · derleme <tarih saat>" — teslimi buradan doğrula.
- Kullanıcı ayarı: `%APPDATA%\StatikRaporClaude\` (son-proje.txt, kapak). Doğrulama komutları ayarı değiştirmez.

## Doğrulama komutları (exe = `bin\Release\net10.0-windows\StatikRaporClaude.exe`)
| Komut | İş |
|---|---|
| `--veri <st4> <rapor.txt>` | Veri/ tabloları (kiriş/kolon/döşeme statik-betonarme, yükler, radye) ↔ GPR hücre hücre |
| `--uretec <st4> <rapor.txt> [kalem süzgeci]` | Uretec/ satır üreteçleri ↔ kalemin GPR bölümleri (veri satırları çoklu küme). Satır bazlı: tek hücre farkı satırı "farklı" sayar |
| `--pdf-gprsiz <st4> <pdf> [hepsi]` | GPR okumadan rapor; yanına `.txt` (kalem → üretilen bölümler / pasif nedeni) |
| `--pdf <st4> <pdf>` | Arayüz varsayılanıyla GPR'li rapor |
| `--ekran <st4> <png> [sekme]` | Pencere görüntüsü |
| `--png <st4> <klasör> [sayfalar]` | GPR sayfalarının PNG'si |

PDF sayfasını PNG'ye çevirmek için poppler yok; Windows.Data.Pdf ile PowerShell betiği kullanıldı (tek sayfa/çağrı).

## Test projeleri (salt okunur — kopyalayarak test et)
- N_ULK_02 — `D:\projeler\projeler2026\ahmet_kustepe\nevzat_ulker\STATIK\N_ULK_02.ST4` (ana referans; GPR 1518 s., FEA3D/metraj/özel tablolar basılı, radye var). Kullanıcının açık projesi aynı klasörde `N_ULK.ST4` (radye sonucu yok).
- K_BLG_03 — `D:\projeler\projeler2026\oncan_mimarlik\bilgehane\statik\K_BLG_03.ST4` (eleman no tabanı 1000, bodrum, perde).
- CP_03 — `...\furkan_mimar\celalettin_pulluk\statik\CP_03.ST4` (poligon kolon); VKF_01 — `...\ali_babaoglu\vakiflar\statik\VKF_01.st4`; FHM_GO_A — `...\ahmet_kustepe\fhm\genc_osman_td\statik\FHM_GO_A.ST4`; A_02 — `...\oncan_mimarlik\beysehir\statik_konut\a_tipi\A_02.ST4`; H_DVR_te_03 — `...\oncan_mimarlik\hasan_duvarci\statik\H_DVR_te_03.ST4` (FEA3D 20 kombinasyon).
- Not: CP_03, FHM_GO_A, VKF_01'de GPR, BEA'dan eski → bazı farklar sürümden.

## Mimari
```
Proje/ProjeKlasoru     klasör, dosya varlığı (DosyaVar), ST4 eleman varlığı
Gpr/                   GPR okuma (Windows-1254, ¡ hücre ayracı, STA kutu karakterleri), bölümlere ayırma, GrupSirasi
Dizgi/                 GprAyristirici (kutu satırlarından tablo/başlık/anahtar-değer), BolumDizgi, Akis (sayfalama)
Modern/                deprem raporu (GPR'siz: DepremRaporu.Dosyadan), kiriş/kolon betonarme modern tablolar
Veri/                  StaVeri (BEA/KMX/COL/SMX/FLO/DMX/LOA okur), St4Model, DosyaTablolari, DosyaBolumleri,
                       TasarimHesaplari (Wk, döşeme δt, perde zarfı), RadyeTablolari (RAD + DE1), GprKarsilastirma
Uretec/                GPR biçiminde satır üreten üreteçler (Proj = dosya erişimi, Fmt = STA4 sayı biçimi)
                       SanalBolum.Ureticiler: "URETEC …" anahtarı → üreteç; SanalBolum.Yaklasik: yaklaşık bölüm notları
Rapor/RaporKatalogu    STA4 yazdırma sekmelerine denk kalemler; DosyaUreticileri (kalem → sanal bölümler),
                       GerekenDosyalar, Zorla (sanal bölüm No ≥ 900000), Sirala, PasifNedeni (kırmızı gerekçe)
Rapor/RaporDerleyici   PDF; sanal bölüm: SanalBolum.Doldur → BolumDizgi, ya da DosyaBolumleri/ModernBolumler
UI/AnaForm             sekmeli seçim; yeşil "(dosyalardan)", kırmızı pasif gerekçesi
```

### Yeni GPR'siz bölüm eklemek
1. Biçimi GPR'den çıkar (`docs/statik-rapor-arastirma/` önce bak). Değer kaynağını bul, en az iki projede hücre hücre doğrula.
2. `Uretec/<Dosya>.cs` içinde `public static List<string> X(Proj p)`; üretemezse `null`. Satırlar GPR ham metni gibi: ilk satır bölüm başlığı, tablo `Ú…¿` üst, başlık `¡` satırları, `Ã…´` ayraç (başlık bitişi), veri, `À…Ù` alt. Kutu karakterleri: Ú Â ¿ Ã Å ´ À Á Ù Ä, hücre ¡; δ/θ = ë/é, ω = ì, Σ = ä, ρ = ï.
3. `SanalBolum.Ureticiler`'e `"URETEC …"` anahtarıyla ekle; yaklaşıksa `Yaklasik`'a neden yaz.
4. `RaporKatalogu.DosyaUreticileri`'ne kalem adı (aynı ad iki sekmedeyse `"Sekme|Ad"`) → (anahtar, başlık, grup); `GerekenDosyalar`'a uzantılar.
5. Release derle, `--uretec` ve `--pdf-gprsiz` ile kontrol; modül durum dosyasını güncelle.

## Durum (N_ULK_02, GPR'siz)
- Varsayılan seçim 101 s., tüm kalemler 1883 s. Örnek: `output/StatikRaporClaude/N_ULK_02_GPRSIZ.pdf`.
- Birebir: Veri tabloları (8/8), radye (deplasman, zemin gerilmesi, plak/mesh, nokta yükü, Winkler, zemin kontrolü), genel bölümler (giriş, yük analizi, opsiyon, program, kombinasyon, aks, kat deprem momenti, devrilme), FEA3D (tüm değer tabloları), özel tablolar (süneklik, temel sünek tesir kolon satırları, imar, yorum, alan oranı), metraj kalıp/beton + temel donatı listesi, duvar yükleri, döşeme şerit, kiriş bilgileri, temel kolon yükleri.
- Yaklaşık: döşeme bilgileri dmin; kiriş sehim-çatlak uç T/M; kolon/perde kesme güvenliği (Mr, Ve tavanı, perde β); kiriş burulma Tcr/Tr, askı Vd; burkulma (Δi dosyada yok → Σ(Δi·Nd/L), Vfi, é boş, k ±0.0007); perde-diyafram Asb/Vr son hane; temel sünek momentlerinde perde Mp; dengeli donatı ρ−ρ' son hane; yapı nokta deplasmanlarında rüzgar/eksantrik satırlar (965/1015).

## Açık işler (öncelik sırasıyla öneri)
1. Kullanıcı arayüzde deneyecek; geri bildirimi bekleniyor (yeşil/kırmızı kalemler, sanal bölüm notları).
2. Burkulma: göreli kat ötelemesi Δi kaynağı (EAR/DIS'ten türetme denenmedi tam olarak) → Vfi, é, βs.
3. Kolon kesme güvenliği Ve tavanı; perde seçim kuralı (K_BLG_03 GPR 140 perde satırı, biz 104).
4. Kiriş sehim uç T/M: STA4'ün kullandığı yükleme (LOA servis yükü + kirişe oturan kiriş).
5. Temel: hareketli yük azaltma katsayısı f (3 kattan fazla), "Temellerin kayma kontrolu" alt tablosu, panel uçları.
6. Radye Mx/My (%96.7), analiz sonuçları/temel donatısı, radye zımbalama, temel kiriş statik.
7. Sürekli/tekil temel statik-betonarme-zımbalama, bağ kirişi betonarme (GPR'siz yok).
8. Metraj: beton/kalıp özetinde TEMEL satırı; donatı keşif metrajı (çubuk boyları dosyada yok — STA4 çizim algoritması gerekir; muhtemelen yapılmaz).
9. FEA3D koordinat/deplasman: "yalnız constraint noktaları" seçeneği (eski sürüm davranışı) dosyada bulunamadı.
10. Poligon kolon boyuna donatısı (PMX) "—" basılıyor.

## Dikkat
- Araştırma araçları (scratchpad `ara_*\tool`) geçicidir; raporlar `docs/statik-rapor-arastirma/`'ya kopyalandı, araçlar kopyalanmadı. Raporlardaki "scratchpad\…" yolları artık olmayabilir.
- `git status`'ta StatikRaporClaude/ ve docs/statik-rapor-arastirma/ commit edilmemiş; commit kullanıcı isteğiyle.
- Aynı depoda başka ajanların (ST4_Plan_ID_Ciz, ST4_Yardimci, NETLOAD) değişiklikleri var; onlara dokunma.
