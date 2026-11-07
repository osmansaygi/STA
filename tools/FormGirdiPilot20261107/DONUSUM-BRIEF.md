# Form dönüşüm brifi (ajanlar için)

Proje: `C:\Users\BEYKENT\Documents\CURSOR\Osman_donusum`. AutoCAD 2027 eklentileri, C# WinForms, net10.0-windows (Yardımcı ve Plan ID). Kullanıcı Türkçe; kod yorumları Türkçe.

## Amaç
Kullanıcı, bütün Plan ve Yardımcı formlarının veri girişlerinin aynı ortak bileşen ve kurallarla, birbiriyle uyumlu olmasını istedi. Örnek tamamlanmış dönüşüm: `ST4_Yardimci/TemelHatiliForm.cs` (önceki hali: `tools/FormGirdiPilot20261107/yedek-20261107/ST4_Yardimci/TemelHatiliForm.cs`). İkisini karşılaştırarak deseni öğren.

## Ortak bileşenler (DEĞİŞTİRME) — `Ortak/Monolit/`, namespace `Beykent.Monolit.Girdi`
- `OlcuKutusu(ad, birim, min, max, varsayilan, ondalik=0, adim=1)` — `.Deger` (double), `.Gecerli`, `.Text`; birim kutunun içinde; ↑/↓ adım; virgül/nokta. `AdimDegeri` ayarlanabilir.
- `DonatiKutusu(ad, cap, aralik, minCap=8, maxCap=32, minAralik=5, maxAralik=50)` — `.CapMm` (int), `.AralikCm`, `.Ayarla(cap, aralik)`; ø sabit önek, kullanıcı `12/15` yazar; yanında cm²/m.
- `SegmentSecim(params string[])` — `.Secili` (int), `.SeciliDegisti`.
- `CapIsaretiSecim(kayitli)` — `.Deger` ("ø","Ø","%%c","Φ"); ComboBox türevi.
- `GirdiDenetci` — `.Ekle(alan)`, `.SegmentIzle(seg)`, `.HataKurali(Func<string>, alanlar)`, `.UyariKurali(Func<string>)`, `.Onayla()` (çiz/tamam öncesi; false ise dur), `.Uygun`, `.Serit` (forma `ui.Section(null).Control(Denetci.Serit, 44)`), `.Guncelle()` (Finish sonrası çağır).
- `MonolitForm.BeginCompact(width)` → `CompactLayout` (`Ortak/Monolit/CompactLayout.cs`): `Section/Field/Hint/Control/Options/Choices/Footer/Tip/When/FieldWhen/Finish`. Formlar zaten bunu kullanıyor.
Bileşende eksik görürsen Ortak dosyalarını DÜZENLEME; formda yerel çöz ve raporda yaz.

## Kurallar (README.md "Ortak form kuralları")
1. Sayı girişi = `OlcuKutusu`; etiketten "· cm" gibi birim ekini kaldır (birim kutuda). Serbest `TextBox`+`TryParse` ve `NumericUpDown` sayı alanları dönüştürülür. Sınırlar mevcut NumericUpDown Min/Max veya mevcut doğrulamadan alınır — FİZİKSEL SINIRLARI DEĞİŞTİRME. Ondalık sayısı korunur.
2. Çap + aralık çifti = `DonatiKutusu` (sınırlar mevcut kontrollerden). Yalnız çap olan alan (aralıksız) mevcut çap ComboBox'ı olarak kalır; serbest sayı kutusundaki çap standart çap listesi ComboBox'ına çevrilir.
3. 2–5 seçenekli birbirini dışlayan seçim (RadioButton grubu, 2–5 öğeli DropDownList seçim, elle dışlanan tikler) = `SegmentSecim`. Bağımsız açık/kapa seçenekleri CheckBox kalır. Uzun/değişken listeler ComboBox kalır.
4. Pafta ölçeği her formda `SegmentSecim("1/50 pafta", "1/100 pafta (×2)")`; Yardımcı'da kayıt `BaPaftaOlcek.Yuz/Kaydet(anahtar)`, Plan'da formun mevcut kayıt yolu korunur.
5. Girdi hatası `GirdiDenetci` şeridinde; girdi doğrulama için MessageBox kaldırılır (çalışma/işlem hatası MessageBox'ları kalabilir). Sessiz varsayılana dönüş kaldırılır (hatalı değer kırmızı görünür, komut çalışmaz).
6. Alt satır `ui.Footer(birincil, kapat, ikincil...)`; Enter = birincil (aktif kontrol Button değilse), Esc = Kapat — mevcut davranış farklıysa koru ama tutarlı hale getir. Birincil eylemden önce `if (!Denetci.Onayla()) return;`.
7. Grup sırası: geometri → donatı → çizim/pafta → katman/diğer. Koşula bağlı alanlar `FieldWhen`/`When` ile gizlenir (ör. sehpa kapalıyken sehpa alanları).
8. Bir ayar tek formda sorulur — yalnız aşağıda grubuna özel yazılan kaldırmaları yap ve davranışın korunduğunu (ayarın başka yerden aynı kaynaktan okunduğunu) kanıtla; kanıt yoksa kaldırma, raporla.

## Mutlak kısıtlar
- Settings sınıflarının property adları, birimleri, ini anahtarları ve okuma/yazma anlamı AYNI kalır (eski kayıtlar açılmalı). Runner/komut dosyalarını değiştirme. `ShowOrActivate/Shutdown/HideTemporarily/BaFormSession/çizimden seçim` akışları aynı kalır. Hiçbir özellik kaybolmaz.
- Yalnız grubuna verilen form dosyalarını düzenle. Başka dosyaya dokunma (Ortak, csproj, runner, diğer formlar, NETLOAD, bin/obj).
- AutoCAD nesnelerine dokunan kodu değiştirme. Genel try/catch ekleme.
- Derleme: kendi grubunun klasöründeki betiklerle (`tools/FormGirdiPilot20261107/derleme-<GRUP>/BuildYardimci.ps1` veya `BuildPlan.ps1`) `powershell -NoProfile -ExecutionPolicy Bypass -File ...`. Başka ajanlar paralel olarak başka formları düzenliyor: hata SENİN dosyanda değilse yok say, birkaç saniye sonra yeniden dene. Kendi dosyalarında 0 hata şart. (pwsh ve python yok.)
- Commit yapma, yayın yapma, NETLOAD'a kopyalama yapma.

## Rapor (en fazla ~60 satır)
Form başına: neyi neye çevirdin, kaldırılan/birleştirilen alan ve kanıtı, yeni hata/uyarı kuralları, koruduğun özel davranışlar, kalan risk. Son satır: derleme sonucu (kendi dosyalarında hata sayısı).
