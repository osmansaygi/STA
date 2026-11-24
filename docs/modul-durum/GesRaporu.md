# GES çatı statik hesap raporu (GesRaporu)

- Klasör: `GesRaporu/` (net10.0-windows WinForms, PDFsharp 6.2, ACadSharp 3.1.12). AutoCAD gerekmez.
- Kısayol: masaüstü "GES Hesap Raporu.lnk" → `GesRaporu/bin/Uygulama/GesRaporu.exe` (Release derlemesi oraya kopyalanır), simge `Varliklar/ges.ico`.
- Şablon: kullanıcının "ÖRNEK ÇATI GES HESABI.pdf" (kar/rüzgar, trapez kaplama, bulon, Sigma 80x55x1.2 profil) + `D:\projeler\00_sablon\GES_ORN.docx` (işin konusu metni, vida tahkiki).

## DWG'den okunanlar (`DwgBilgi.cs`)
- Dosya AutoCAD'de açıkken de okunur (FileShare.ReadWrite + bellek akışı).
- Kapak: blok içi MText etiket/değer eşleşmesi (MAHALLE, PAFTA / ADA / PARSEL, CİNSİ, KAT ADEDİ, KULLANIM AMACI, BLOK ADI, YAPI/MAL SAHİBİ, İL/İLÇE, MİMARİ PROJE MÜELLİFİ alt satırı).
- İl: tüm yazılarda en çok geçen il adı. İlçe: yazı → yoksa `%APPDATA%\GesRaporu\mahalle-ilce.csv` (öğrenir; HOCACİHAN→SELÇUKLU tohum).
- Kar bölgesi: `%APPDATA%\GesRaporu\kar-bolgeleri.csv` (KONYA;*;7 tohum; raporda girilen il/ilçe/bölge öğrenilir).
- Rakım: KOT bloklarındaki (mutlak − bağıl) çiftlerinin ortancası (±0.00 mutlak). Bina yüksekliği: kesit dışı KOT bloklarında en büyük bağıl kot.
- Çatı planı: "ÇATI PLANI" başlığı (yoksa ÇATI KATI) → sağ/solundaki uzun düşey pafta çizgileri arası; ölçü/yazı/tarama/tefriş katmanları hariç; %3–97 merkez dilimiyle antet ayıklanır; vektör olarak PDF'e çizilir. Panel boyutunda dikdörtgen/blok varsa sayılır.
- Eğim: mahya kotu − plan bölgesindeki en düşük kot / (kısa kenar/2); bulunamazsa girilen değer.

## Hesap (`Hesap.cs`)
- Kar TS498:2021 Çizelge 3 (ara rakımda üst satır, >1000 m %10, >1500 m %15). Konya 1062 m bölge 7 → 1,40×1,10 = 1,54 kN/m² (1 kN = 101,97 kg).
- Rüzgar q = max(Çizelge 4(H), v²/1600 ile 130 km/sa); W1=(1,2sinα−0,4)q, W2=−0,4q.
- Kaplama P=1,4g+1,6S ↔ Panelsan 38/151 ve 34/250 tablosu (açıklıkta doğrusal ara değer) veya kullanıcı kapasitesi.
- Bulon: Wrüz=1,15·W·a·b·0,5; N=min(τπd²/4, d·t·σl, σzπd²/4). Vida: P/n, çekirdek π(0,8d)²/4, ezilme kaplama sacı.
- Profil: açıklık ql²/16, mesnet ql²/11, sehim 5ql⁴/384EI (örnekteki 1/140 yerine güvenli taraf), l/300.

## Doğrulama (23.11.2026)
- `GesRaporu.exe --test <dwg> <pdf> [sahip]` (arayüzsüz) ve `--formtest <dwg> <txt>` (arayüz akışı).
- `ms_a_blk.dwg`: KONYA/SELÇUKLU/HOCACİHAN, 16555/1, A blok, B+Z+7, konut, müellif; rakım 1062, H 27,05, bölge 7, Sk 1,54; 12 sayfa PDF (2 katalog eki dahil) görsel kontrol edildi.
- Açık: DWG'de yapı sahibi yok (elle girilir); başka il için kar bölgesi ilk seferde seçilmeli (sonra öğrenilir). Panel yerleşimi çizimde yoksa adet elle girilir.

## 1.1 (23.11.2026)
- Raporda ve formda mimari proje müellifi yok (DWG'den okunmaya devam eder, kullanılmaz).
- `Hesap.Kurtar`: yetersiz eleman raporda yer almaz. Sıra: kaplama (aşık aralığı korunarak sac 0,50→0,70, sonra aşık 2,5→1,0 m) → bulon (çap M8…M16, profil t 2–4 mm, adet 1–4) → vida (adet 2…8, çap 5…8 mm) → profil mesnet aralığı 5 cm adımlarla (≥10 cm). Form değişen alanları turuncu işaretler ve listeyi gösterir; çözülemezse rapor üretilmez. `Rapor.Uret` de aynı kurtarmayı uygular.
- Test: `GES_ZOR=1` ortam değişkeniyle `--test` → 3 yetersiz eleman kurtarıldı, PDF'de tümü YETERLİ; normal projede değişiklik yok.
- 1.2: trapez sac kurtarması önce aşık aralığını 50 cm adımlarla düşürür (ör. 2,5→2,0→1,5→1,0→0,5 m, en az 0,50 m), sac kalınlığı yalnız bu yetmezse artar. 1,00 m altı kapasite tablo dışı: 1,00 m değeri × 1,00/L (güvenli taraf), raporda belirtilir. Zor test: 0,50 mm sac 2,5 m → 0,5 m aşık, 333/570 kg/m² yeterli.

## Bağımsız panel bilgi föyü (23.11.2026, Codex)
- `output/pdf/Gunes_Paneli_Fiziksel_Ozellikler_2095x1039x40.pdf`: tek A4 sayfa; boy/en/kalınlık, ağırlık, ürün görseli, katalog arka görünüşü ve şematik yan görünüş.
- Kaynak: TommaTech 144PM üretici kataloğu (ENF arşivi), 2095×1039×40 mm, 24,5 kg/adet. Kullanıcı marka/model belirtmediğinden referans ürün olduğu belgede yazılıdır.
- Doğrulama: tek sayfa, Türkçe metin, boyut ve ağırlık kontrolü; Poppler PNG çıktısı görsel incelendi. Program/C#/DLL/NETLOAD değişikliği yok.

## 1.3 (23.11.2026) — StatikRapor görünümü + çoklu blok
- Rapor görünümü StatikRaporClaude ModernSayfa/KapakSayfasi ile aynı dil: Segoe UI, renkli BEYKENT logosu (Varliklar/beykent.png, Logo.cs mantığı PdfYazici.cs içinde), lacivert/kobalt/turuncu, her sayfada logo + proje başlığı + blok rozeti, alt bilgi "n / N", kart düzenli kapak (ARSA, BİNA, GES SİSTEMİ, SONUÇ, HAZIRLAYAN + İMZA/KAŞE, YAPI SAHİBİ), sayfa numaralı içindekiler, özet kutucukları (yükler, kullanım oranları), zebra satırlar.
- Çoklu blok: her "BLOK ADI" kapak etiketi bir blok (`DwgBilgi.Bloklar`); kapak alanları o kapak çevresinden okunur; plan başlıkları en yakın kapağa (önce aynı yatay hiza) atanır. Birden çok blok varsa form "Blok seçimi" penceresiyle sorar; üstte Blok listesi var. Bina yüksekliği plan paftasının düşey bandındaki kotlardan (blok bazında). Proje girdileri DWG + blok anahtarıyla saklanır. Dosya adı ..._A_BLOK.pdf.
- Çatı planı çıkarımı: pafta çerçevesi negatif koordinatta da bulunur (Y2>0 hatası giderildi), eğik çerçeve çizgisi toleransı; paftadan taşan kat planı blokları açılıp yalnız pafta içi çizgiler alınır; YAZI ve ÖLÇÜ katmanındaki çizgiler artık dahil (mahya/kırma çizgileri bu katmanlarda çiziliyor), MAHAL/KOT blokları hariç.
- Test DWG: mimari_20261123 REVİZE 4 → 4 kapak (A, B, KAPALI OTOPARK, GÜVENLİK K.); A: H 25,92 m, B: H 23,20 m; iki rapor da görsel kontrol edildi. `GES_BLOK`, `GES_DEBUG` ortam değişkenleri yalnız `--test` içindir.

## Bağımsız trapez sac / GES uygulama föyü (23.11.2026, Codex)
- `output/pdf/Trapez_Sac_GES_Turkiye_Uygulama_Raporu.pdf`: tek A4; 38/151 örnek çelik profil görseli/ölçülü kesiti, 0,50/0,60/0,70 mm için üretici ağırlıkları ve 3 m levha örneği; GES kısa ray, EPDM, semer ve ayrı sac-aşık bağlantısı şeması.
- Altı tıklanabilir kaynak: Astek, Starmet, Assan Panel, K2 MiniRail MK2 V18/0725, ÇŞİDB çelik örnekleri (basılı s. 3), YFK su yalıtımı (s. 54). Türkiye kar/rüzgâr kontrolleri, bini/sızdırmazlık, korozyon ve yoğuşma özetlendi. K2 0,40 mm/S235 ve tepe ölçüsü sınırları yalnız sistem örneği; genel kalınlık önerisi değildir.
- Doğrulama: pypdf ile tek A4, sayısal değerler/Türkçe metin ve 6 kaynak bağlantısı PASS; son Poppler PNG görsel kontrolü PASS. Gerçek sac tipi ve saha bilgileri verilmediğinden sahaya özel yeterlilik hesaplanmadı. Program/C#/DLL/NETLOAD değişikliği yok.

## 1.4 (23.11.2026) — sabit ekler
- EK-1 güneş paneli fiziksel özellikleri ve EK-2 GES altında trapez sac (kullanıcının Codex'te sadeleştirdiği PDF'ler) `GesRaporu/Varliklar/ekler-sabit/` altında; her rapora sabit gömülür (`Rapor.SabitEkler`). Kaynak sayfanın koyu başlık bandı (%11,6) ve alt "1/1/kaynak" satırı kırpılır, yerine rapor başlığı "EK-n …", logo, blok rozeti ve n / N gelir (`PdfYazici.EkGom`, XPdfForm + kırpma).
- Formdaki ek listesi yalnız ilave ekler içindir (EK-3, EK-4 …); bunlar da aynı başlıkla, kırpmasız gömülür. Eski CWT/Panelsan katalog ekleri kaldırıldı; eski kayıtlı yollar formda süzülür.
- 1.5 (23.11.2026): kapakta 'Mimari proje: …dwg' satırı ve İMZA/KAŞE kutusu kaldırıldı, HAZIRLAYAN kartı tam genişlik. Logo: B işaretinin alt parçası taşma doldurmayla bulunup tamamen yeşil (eski düz y=270 sınırı kavisli ucu mavi bırakıyordu). Aynı hata StatikRaporClaude/Modern/Logo.cs'de duruyor (dokunulmadı).

## 1.6 (24.11.2026) — kapak okuma yeniden yazıldı (`DwgKapak.cs`)
- 9 farklı büronun mimari projesi incelendi (vakıflar, adem tafli, bezirci, celalettin pulluk, selman kon, atahan akay, ardıçlı 43704-1, 18171_3 B blok, aslım ticaret) + ms_a_blk ve REVİZE 4.
- Yazı geometrisi: MText bağlama noktası ve TEXT hizası ile gerçek satır ortası (Yc) ve tahmini yatay kapsam (X0–X1); blok örneği kimliği (Grup); görünür blok öznitelikleri artık yazı (PROJE KAPAK bloğu değerleri özniteliklerde), blok tanımındaki öznitelik şablonları yok sayılır. 90° dik yazılar ayrı tutulur.
- Etiket eşleşmesi ASCII/harf-yalnız anahtarla: PAFTA / ADA / PARSEL = PAFTA-ADA-PARSEL = PAFTA,ADA,PARSEL = Pafta / Ada / Parsel; İl/İlçe/Mahalle/Sokak, MAHALLESİ / İLÇESİ, YERİ, İnşaat Türü, İnşaat Kat Sayısı, PROJENİN SAHİBİ, SAHİBİ … (sözlük `EtiketAlani`).
- Değer yeri sırası: aynı satırda sağdaki ilk yazı (etiketse değer yok) → sütun başlığı satırıysa altındaki değer → sahip için kutuda altta/üstte, dik yan etiketin sağında; "ADI SOYADI", unvan (MİMAR) atlanır; diğer sütunun değeri en yakın başlığa göre elenir. Etiket blok içindeyse önce aynı blok aranır (kapak plan üstüne bindirilmiş olsa da gürültü girmez).
- Birleşik değerler: PAFTA/ADA/PARSEL sondan iki sayı (M28b05b2a-43704- 1, -/ 15644 / 7, 26870/5, .... - 33379- 1); AZİZİYE/ KARATAY, SELÇUKLU/YAZIR (ilçe listesiyle ayrılır), ARDIÇLI MAH. → ARDIÇLI. Kapak tablosu eksikse başlık cümlesi: "KONYA İLİ MERAM İLÇESİ MELİKŞAH MAHALLESİ 2645 ADA 103 PARSEL".
- Çoklu kapak: her PAFTA/ADA/PARSEL (yoksa MAHALLE, yoksa BLOK ADI) etiketi bir kapak; blok adı BLOK ADI'ndan, yoksa kapak çevresindeki "A BLOK" / "MİMARİ PROJE (A BLOK)" yazısından; her paftada tekrarlanan aynı kapaklar birleştirilir; adsız kapak kullanım amacıyla (TRAFO, GÜVENLİK) adlanır.
- Kapak anahtarları artık ayrı: PAFTA, ADA, PARSEL (eski birleşik "PAFTA/ADA/PARSEL" anahtarı kalktı; AnaFormBlok ve --test uyarlandı).
- İlçe: kapak → mahalle tablosu (+ bilinen Konya mahalleleri: Hocacihan, Yazır, Sille, Melikşah, Lalebahçe, Hacı Şaban, Aziziye, Fevziçakmak, Ardıçlı) → adres/iletişim satırları dışındaki yazılarda en çok geçen ilçe (eskiden büro adresindeki MERAM seçiliyordu). İl/ilçe seçilen bloğa göre yeniden belirlenir.
- Denetim aracı: `GesRaporu.exe --kapak <cikti.txt> <dwg…>` tüm kapakları listeler.
- Sonuç (eski → yeni): ada/parsel 5 projede boştu → hepsi dolu; mahalle 4 projede boştu → dolu; sahip 0/9 → 6/9 (diğer 3 çizimde kapakta sahip yazılı değil); selman kon CİNSİ/KAT "İNŞAAT ALANI" hatası, ardıçlı Kat/Kullanım "A" hatası giderildi; ardıçlı 6 blok + trafo + güvenlik, aslım A/B/C ayrı kapak.
- Sahip etiketi varyantları: PROJE SAHİBİ / PROJENİN SAHİBİ (en yaygın), PROJE SAHİPLERİ, PROJE SAHİBİ ADI SOYADI, ARSA SAHİBİ …

## 1.6 ek (24.11.2026) — bina yüksekliği
- Kot kaynağı genişletildi: kot bloğu öznitelikleri + düz yazı kotları ("+14.00", "±0.00", "-3.20", "KOT: +12,50", "MAHYA KOTU +14.20"). KESİT adlı blokların kotları ayrı tutulur ve yalnız son çare olarak kullanılır (ms_a_blk'te +59,89 gibi başka referanslı değerler var).
- En büyük kot: komşusundan 6 m'den fazla kopuk tek tük üst değerler atılır (`UstKot`).
- `DwgBilgi.BinaYuksekligi(plan, out kaynak)`: çatı paftası kotu → çizimdeki en büyük kot → kesit kotları → KAT ADEDİ tahmini (zemin üstü kat × 3,0 m + 1,0 m). Kot, tahminin 0,75–2,0 katı dışındaysa reddedilir (paftada yalnız bodrum kotları). Tahmin kullanılırsa formda "kontrol edin" notu.
- Sonuç (eski → yeni H, m; Release exe ile 11 DWG): pulluk B+Z+9 10 (varsayılan) → 33,85; atahan 2B+3 −5,30 → 14,00; bezirci B+Z+1 10 → 9,31 (kesit); 18171_3 B+Z+1 10 → 10,90; aslım 10 → 11,45; ardıçlı A 1,40 → 23,40 (çizim geneli); selman kon 6,20; vakıflar 24,06; adem tafli 9,35; ms_a_blk ve REVİZE 4 A 25,92 → 26,77 (pafta içi düz yazı kotu).

## 1.6 ek 2 (24.11.2026) — çatı planı seçimi ve penceresi
- Varsayılan başlık: ölçek yazan (Ö:, ÖLÇEK, 1/100) "ÇATI PLAN…" önce; ölçeksiz "ÇATI PLAN" çoğu zaman kesitteki seviye etiketidir (43704-1'de yanlış seçiliyordu).
- Başlık bir pafta bloğunun özniteliğiyse ve pafta çerçeve çizgisi bulunamazsa pencere o blok örneğinin sınırıdır (`Yazi.Kutu`).
- Pencere içinde başka plan başlığı varsa daraltılır: üstteki başlığın altına kadar; yandaki ölçekli başlıkla ortadan bölünür (bir paftada çatı kat planı + çatı planı).
- Sonuç: 43704-1 A blok 59×1,5 m (kesit şeridi) → 81×33 m kırma çatı planı; selman kon 20,7×28,3 → 20,7×17,6 m (üstteki çizim artık girmiyor); aslım 51×30 → 48×27 m (çerçeve kenarı daraldı, plan tam). Diğer 8 çizimde plan/yükseklik değişmedi.
- Doğrulama görseli: `--test` ile `GES_SVG=<yol.png>` ortam değişkeni çıkarılan çizgileri PNG çizer.
