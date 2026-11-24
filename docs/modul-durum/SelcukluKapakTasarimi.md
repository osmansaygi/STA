# Selçuklu A4 kapak tasarımı - 24.11.2026

- Bağımsız tasarım teslimi: BEYKENT PROJE mevcut siyah logosuyla 10 farklı A4 kapak; proje ve statik hesap raporu için iki ayrı 10 sayfalık vektörel PDF.
- Kaynak: `D:/projeler/00_sablon/SELCEKLU_STATIK_KAPAK.dwg`, kullanıcı tarafından işaretlenen e-imza alanları. DWG geometrisi çalışma kopyasında AutoCAD Core Console 2027 ile salt okunur çıkarıldı. Kaynak ve çalışma kopyası SHA256 eşit.
- Çıktılar: `output/pdf/Selcuklu_Kapak_10_Secenek.pdf`, `output/pdf/Selcuklu_Rapor_Kapak_10_Secenek.pdf`; `output/selcuklu-kapak-473a/` içinde 10 ayrı PNG, iki karşılaştırma JPG'si ve `dogrulama.json`. Yeniden üretim: `tmp/pdfs/selcuklu-kapak-473a/kapak_uret.py`.
- İmza kutuları A4 sol üstten mm: (7,180,64.4,42), (73.4,180,63.2,42), (138.6,180,64.4,42); başlık altı y=192. Belediye iç kutusu (7,228,196,37), başlık altı y=235. Çerçeveler ve boş imza yüzeyleri bütün seçeneklerde aynı; dekoratif unsurlar bu alanlara girmez.
- Şablondaki arsa/yapı bilgi başlıklarının tamamı var; proje/maliyet/kimlik değerleri örnek boşluklardır. Resmi belediye amblemi eklenmedi. Selçuklu geometrisi dekoratif çizgidir.
- Doğrulama: her iki PDF 10 adet 210x297 mm sayfa; gerekli Türkçe başlıklar/metinler PASS; Poppler görsel kontrol; 10 proje kapağında imza ve belediye alanı piksel karşılaştırması PASS.
- C# testi, derleme, DLL kopyalama: kapsam dışı/yapılmadı. Canlı AutoCAD açma/yazdırma ve yeni DWG üretimi: yapılmadı. Core Console yalnız kaynak okuması için çalıştı. Uygulama/NETLOAD değişmedi.
- Durum: 10 görsel seçenek teslim edildi. Seçilen tasarımın düzenlenebilir DWG/rapor uygulamasına aktarımı bu görsel seçim aşamasının sonrasındadır; belediye kabulü iddia edilmez.
