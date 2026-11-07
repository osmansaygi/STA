# GPR'siz STA4 rapor bölümleri — ortak araştırma bağlamı

Amaç: STA4CAD'in yazdırma anında ürettiği GPR raporundaki bölümleri, GPR'yi HİÇ okumadan,
proje klasöründeki analiz/sonuç dosyalarından (ve gerekirse STA4 kurulumundaki C:\Sta4V14 dosyalarından)
birebir üretebilmek. GPR yalnız DOĞRULAMA için kullanılır (doğruluk kaynağı).

## Doğruluk kaynağı (salt okunur — ASLA yazma)
- Proje: D:\projeler\projeler2026\ahmet_kustepe\nevzat_ulker\STATIK\N_ULK_02.ST4
- Tam GPR (tüm seçenekler, 1518 sayfa): ...\N_ULK_02.GPR  (Windows-1254 metin)
- Diğer doğrulama projeleri (her biri ST4 + GPR + sonuç dosyaları):
  D:\projeler\projeler2026\ali_babaoglu\vakiflar\statik\VKF_01.st4
  D:\projeler\projeler2026\furkan_mimar\celalettin_pulluk\statik\CP_03.ST4
  D:\projeler\projeler2026\oncan_mimarlik\bilgehane\statik\K_BLG_03.ST4
  D:\projeler\projeler2026\ahmet_kustepe\fhm\genc_osman_td\statik\FHM_GO_A.ST4
- D:\projeler altına ve C:\Sta4V14 altına hiçbir şey yazılmaz.

## GPR biçimi (kısa)
- Satırlar Windows-1254. Sayfa başı "no,çizgiSayısı,yazıSayısı"; yazı kaydı "x,y,a,b,tip,font" + sonraki satır metin.
  Basit yol: dosyayı 1254 ile oku, '-?\d+,' ile başlayan koordinat satırlarını at, kalan metin satırlarını incele.
- Tablo hücre ayıracı karakter U+00A1 '¡' (1254'te 0xA1 = dikey çizgi). Kutu çizgi karakterleri Ä Â Å Ã ´ Ú ¿ À Ù.
- Başlıklar düz metin satırı ("  DEPREM RAPORU", "KİRİŞ STATİK HESAP SONUÇLARI" vb.).
- Sayı biçimi: STA4 float32 değeri G7 metne çevirip yarım-yukarı yuvarlar; "-0.00" basmaz.

## Bilinen dosya çözümleri (zaten programda var — tekrar araştırma)
Kaynak kod: C:\Users\BEYKENT\Documents\CURSOR\Osman_donusum\StatikRaporClaude\Veri\*.cs ve Modern\DepremRaporu.cs
Ayrıntılı ofset listesi: C:\Users\BEYKENT\Documents\CURSOR\Osman_donusum\docs\modul-durum\StatikRaporClaude.md
- ST4 metin: /Story/, /Beams Data/ (3 satır/kiriş), /Columns Data/ (3 satır), /Floors Data/ (2 satır), /Polygon columns/, /Polygon section/,
  başlık satırları: 4. satır R (5. alan) I (6.), 7. satır Ko(2.) eksantrisite(4.) β(5.), 10. satır Hbodrum (6.), 14. satır D,Sds,Sd1,Ss,S1,..,enlem,boylam,DD3.
- BEA 900 bayt/kiriş, KMX 300, COL 900/kolon (yük dizileri 22 float: 0..6 düşey düzenler, 7 zemin, 8..13 ±EX ±EY WX WY, 14 ısı),
  SMX 300, FLO 200/döşeme, DMX 250, LOA 32 bayt satır, FeaMod (int[1]=mod sayısı, T double 20+8i, ω 20+8(n+1)+8i, Mxr/Myr float çiftleri son 8n bayt).
- Radye: .RAD metin (2. satır: plak sayısı, düğüm sayısı, eleman sayısı, Ko; düğüm satırları 6 alan "plak,x,y,z,bayrak,yay",
  ilk alanı 0 olmayanlar sırayla 1..n), .DE1 (düğüm kaydı 40+588·(n−1), yük durumu başına 28 bayt, ilk float düşey deplasman m işareti ters;
  zemin gerilmesi = Ko·w; GPR radye tablolarında yalnız yay>0 düğümler). v6 notu: NOKTALARIN X/Y STATİK, WINKLER, NOKTA YÜKLERİ için
  DE1 vaka slotları kısmen eşleşti (eski araştırma çıktısı: scratchpad\gprsiz\verify-v6-radye2.txt).
- FeaVec mod vektörleri: f32 [(dugum-1)*144 + bilesen*24 + mod*4] (82476/82476 doğrulandı, scratchpad\gprsiz\verify-v7-modvec.txt).
- Eski fizibilite araç kodu (fikir için): scratchpad\gprsiz\tool\*.cs; sayfa listesi scratchpad\gprsiz\gpr-sections.txt.

## Çalışma düzeni
- Kendi klasörün: scratchpad\<senin_klasörün>\ (yalnız oraya yaz). C# araç için örnek proje:
  scratchpad\kal2\Kalibre.csproj (StatikRaporClaude.csproj'a ProjectReference ile; GPR satır okuma örnekleri Program.cs içinde).
  Kendi klasörüne kopyala, `dotnet build -c Release -o bin` ile derle. StatikRaporClaude projesinin kaynaklarını DEĞİŞTİRME.
  (Not: ProjectReference ile derleme, StatikRaporClaude\bin\Uygulama'ya da kopyalayabilir; bu sorun değil.)
- Python yok; C# veya PowerShell kullan. Windows; bash'te Git Bash. Türkçe karakterli komutları Edit/Write ile yaz, sed'de ters eğik çizgiye dikkat.
- Her bölüm için çıktı: (1) veri kaynağı ve kesin çözüm kuralı (dosya, kayıt boyu, ofset, ölçek, işaret, satır seçme kuralı, sıralama, sayı biçimi),
  (2) GPR ile doğrulama sonucu hücre/satır sayısıyla (ör. 5940/5940), mümkünse ikinci bir projede de,
  (3) üretilemiyorsa nedeni (hangi değer hiçbir dosyada yok) ve yaklaşık hesap mümkünse bağıntı ve sapma.
- Sabit metin bölümleri (program tanıtım metni vb.) için: metin ST4/OPS/C:\Sta4V14 dosyalarından çıkarılabiliyor mu bak; değilse
  hangi değişken alanların (sayıların) hangi dosyadan geldiğini bul — sabit metni biz kendi cümlelerimizle yazabiliriz.
- Sonuç raporunu kendi klasörüne RAPOR.md olarak yaz; son mesajında özetle (bölüm → kaynak → doğrulama oranı → kural).
