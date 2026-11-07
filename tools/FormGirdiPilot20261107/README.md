# Form girdi pilotu — 07.11.2026

İzole önizleme. Canlı `ST4_Plan_ID_Ciz` / `ST4_Yardimci` projelerine bağlı değildir, NETLOAD'a kopyalamaz, kullanıcı ayarlarına yazmaz (BaIni taklit).

## Bileşenler (`Girdi/`, namespace `Beykent.Monolit.Girdi`)

- `OlcuKutusu` — birim kutunun içinde, min/max denetimi, virgül/nokta kabul, ↑/↓ adım. Sınır dışı değer kırmızı; sessiz kırpma yok.
- `DonatiKutusu` — `ø12/15`, `12/15`, `12-15`, `Ø 12 / 15` yazımı; standart çap denetimi; yanında cm²/m. ↑/↓ çap kademesi.
- `SegmentSecim` — 2–5 seçenekli radyo grubu yerine tek satır.
- `GirdiDenetci` — alt şerit: kırmızı hata (komutu engeller, ilk hatalı alana odaklanır), sarı mühendislik uyarısı (engellemez), yeşil uygun; "Varsayılana dön".

- `CapIsaretiSecim` — çizime yazılacak çap işareti için liste (ø / Ø / %%c / Φ); Donatı Boyu ve Metraj'daki serbest "Fi işareti" kutusunun yerine. Kayıtlı eski özel işaret korunur.

## Ortak form kuralları (bütün Plan ve Yardımcı formları — kullanıcı isteği 07.11.2026)

1. Sayı = `OlcuKutusu`, birim kutunun içinde; etikette "· cm" yazılmaz. Serbest `TextBox` + `TryParse` ile sayı alınmaz.
2. Çap + aralık = `DonatiKutusu` (ø sabit önek, kullanıcı ø yazmaz). Yalnız çap = standart çap listesi.
3. 2–5 seçenekli tek seçim = `SegmentSecim`; birbirini dışlayan tikler kullanılmaz.
4. Pafta ölçeği her formda aynı segment: "1/50 pafta | 1/100 pafta (×2)".
5. Hata ve uyarı alttaki `GirdiDenetci` şeridinde; MessageBox ile girdi hatası gösterilmez, sessiz varsayılana dönüş yok.
6. Alt satır aynı: [ikincil] [birincil] [Kapat]; Enter = birincil, Esc = Kapat. "Varsayılana dön" şeritte.
7. Grup sırası: geometri → donatı → çizim/pafta; az kullanılan alan koşula bağlı gizlenir.
8. Bir ayar tek formda sorulur; başka form aynı ayarı kopyalamaz.

Not: `Ortak/Monolit/*.cs` iki programa joker ile derlendiğinden bileşenler entegrasyon onayına kadar burada tutulur.

## Pilot

`Pilot/TemelHatiliFormYeni.cs` — Temel Hatılı formu. Aynı `TemelHatiliSettings` alanlarını yazar; runner değişmeden kullanılabilir.

## Çalıştırma

```
dotnet build -c Release
bin\Release\net10.0-windows\FormGirdiPilot.exe            # eski ve yeni form yan yana, elle deneme
bin\Release\net10.0-windows\FormGirdiPilot.exe --test     # ayrıştırma + form davranış testleri
bin\Release\net10.0-windows\FormGirdiPilot.exe --shots onizleme-png
```
