# Türkiye mimari plan çizim kuralları (MIMARIKAT için araştırma notu)

30.10.2026. Bu not, MIMARIKAT/MIMARIBOSLUK/MIMARIMERDIVEN algılama eşikleri için hazırlandı. Kaynaklarda bulunmayan, genel uygulamaya dayanan bilgiler "pratik" diye işaretlidir.

## Merdiven ölçüleri (mevzuat)

**Planlı Alanlar İmar Yönetmeliği, Md. 31:**
- Ortak merdiven kolu ve sahanlık genişliği konutta en az 1,20 m, diğer yapılarda en az 1,50 m. Bağımsız bölüm içindeki merdivende en az 1,00 m.
- Rıht yüksekliği asansörsüz binada en çok 16 cm, asansörlü binada en çok 18 cm.
- Basamak genişliği 2a+b = 60–64 formülüyle bulunur ve en az 27 cm olur.
- Balansmanlı (dengelenmiş) basamakta en dar kenar en az 10 cm, basamak ortası en az 27 cm. Basamak burnunda damlalık (çıkıntı) olmaz.
- Ara sahanlık ve kat sahanlığı en az kol genişliği kadardır (Md. 4).

**Yangın yönetmeliği, kaçış merdiveni:**
- Rıht en çok 17,5 cm, basamak genişliği en az 25 cm.
- İki sahanlık arasında 4–17 basamak bulunur ve kot farkı en çok 3,00 m'dir.
- Baş yüksekliği (merdiven üstündeki boşluk) en az 2,10 m.

**Tipik değerler:**
- Konut kat yüksekliği 2,80–3,00 m. Bu, kat başına 16–18 rıht eder. Asansörsüz binada 16 cm sınırıyla 3,00 m kat 19 rıht ister.
- Kol genişliği 1,00–1,50 m, basamak genişliği 25–32 cm.

## Merdivenin mimari planda gösterimi

- **Bakış:** Kat planı döşemeden yaklaşık 1,20–1,50 m yükseklikten alınan yatay kesittir ve aşağıya bakar.
- **Kesilen kol:**
  - Kesit düzlemi kolu yaklaşık ortasından keser ve orada tek ya da çift eğik çizgi (zigzag) çizilir. ideCAD ve ArchiCAD'de bu hattın konumu, açısı ve çizgi aralığı ayarlanabilir.
  - Kesit hattının altındaki basamaklar sürekli, üstündekiler kesikli veya noktalı çizilir.
  - Bu yüzden bir katın mimarisinde bazı rıhtlar eksik ya da parça parça olabilir.
- **Çıkış hattı:**
  - Ok çıkış yönünü gösterir; başlangıçta daire, sonda ok kullanımı pratiktir.
  - Basamak numaraları ve "yukarı/aşağı" yazıları yazılımın ayarına bağlıdır.
- **Diğer ayrıntılar (pratik):**
  - Burun çift çizgi olabilir.
  - Korkuluk/küpeşte paralel çizgilerle gösterilir.
  - Göz çoğunlukla 10–20 cm'dir.

## Boşluklar

**Planlı Alanlar İmar Yönetmeliği:**
- Hava bacası en az 60×60 cm olur ve daraltılamaz. Şönt tipi olamaz; içinden elektrik ve doğalgaz geçemez.
- Işıklık (Md. 32): 1–6 katlı yapıda dar kenar en az 1,50 m ve alan en az 4,50 m². 7 ve üzeri katta dar kenar en az 2,00 m ve alan en az 9 m².
- Asansör (Md. 34): kabin dar kenarı en az 1,20 m, alanı en az 1,80 m². 10 kat ve üzerinde sedye asansörü gerekir (en az 1,20×2,10).
- Şönt baca yapılabilir; ölçüsü yönetmelikte yok. 20×40 kullanımı pratiktir.

**Pratik gösterim:**
- Etiketler: ELK. ŞAFT, TES. ŞAFT, H.B., ŞÖNT, KAZAN BACASI, ASANSÖR, GALERİ BOŞLUĞU.
- Gösterim: kapalı dikdörtgen, çoğu zaman X ya da tek köşegen. Asansör kuyusunda kabin ve karşı ağırlık çizilir.

## Kalıp planı (BEYKENT alışkanlığı, 9 proje ölçümü)

- **Bakış:** Kalıp planı tavana bakar. N. katın kalıbı, N+1. katın mimarisiyle eşleşir.
- **Merdivende DOSEME SINIRI:**
  - Merdivenin tam çevresi çizilmez; duvar ve kiriş boyu zaten kiriş çizgisidir.
  - Yalnız şunlar DOSEME SINIRI'dır: kolların sahanlığa veya kata bağlandığı uç çizgileri ve ters kollar arasındaki göz dikdörtgeni.
- **Rıhtlar:** MERDIVEN katmanına çizilir. Uç çizgileri rıht değildir.
- **Kısmi çizim:** Bazı kalıplarda merdivenin yalnız ilgili döşemeye ait kolları çizilir. Bodrumdan zemine çıkan merdivende kesikli çizim kuralı kullanıcıdan bekleniyor.
- **Boşluklar:** KALIP BOSLUK katmanında, X ile çizilir. Bitişik bacalar tek boşluk yapılır.

## Kaynaklar

- Planlı Alanlar İmar Yönetmeliği (01.07.2026 metni): https://www.alomaliye.com/wp-content/uploads/2017/07/planli-alanlar-01-07-2026.pdf
- Yangın yönetmeliği kaçış merdivenleri (ikincil kaynak): https://yangin110.wordpress.com/yangin-yonetmeligi/ucuncu-kisim-kacis-yollari-kacis-merdivenleri-ve-ozel-durumlar/kacis-merdivenleri/
- ÇŞB yangın yönetmeliği kılavuzu: https://webdosya.csb.gov.tr/db/meslekihizmetler/haberler/b-nalarin-yangin-korunmasi-hakkinda-yonetmel-k-kilavuzu-20250424095505.pdf
- ideCAD kesit hattı: https://help.idecad.com.tr/ideCAD/planda-merdiven-kesit-hattinin-cizilmesi
- ArchiCAD merdiven plan gösterimi: https://bimsoft.com.tr/kutuphane/merdiven-plan-gosterim-ayarlari/
- Merdiven çizimi (örnek ölçüler): https://www.pislikmimar.com/merdiven-cizimi/
- İTÜ teknik çizim ilkeleri: https://web.itu.edu.tr/~ecem/TeknikCizimIlkeleri.pdf
- Kalıp planı tanımı: https://dergipark.org.tr/tr/download/article-file/1175714
