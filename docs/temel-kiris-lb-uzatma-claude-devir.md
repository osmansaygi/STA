# Claude'a devir — sürekli temel kirişlerinde Lb ve donatı uzatmaları

Tarih: 22.10.2026. Kullanıcı için hazırlanan araştırma ve kod inceleme notudur. Bu çalışma C# kodunu veya DLL'leri değiştirmedi. Aşağıdaki kod gözlemleri bu tarihte diskten okunan sürüme aittir; uygulamadan önce güncel dosyayı yeniden oku.

## Claude'a verilecek görev özeti

Sürekli temel açılımındaki Lb, 50Ø, Ln/4, uç gönye ve bindirme hesaplarını aşağıdaki ayrımlarla incele. Mevcut STA4CAD karşılaştırmalarını ve kullanıcının temel ucunda gönye tercihini koru. Çizim benzerliği ile yapısal yeterliliği ayrı doğrula. Bu not bir uygulama projesinin donatı yeterlilik onayı değildir; gerçek malzeme, moment zarfı ve birleşim geometrisiyle eksik kontroller belirlenmelidir. Genel kiriş davranışına görev dışı değişiklik yapma.

Önce `docs/ajan-koordinasyon/PROTOKOL.md`, aktif oturumlar, `docs/csharp-autocad-calisma.md` ve ilgili modül durumunu oku. Kaynak dosyanın başka oturumda ayrılmış olması halinde koordinasyon protokolünü uygula.

## 1. Kaynaklardan çıkan esas ayrımlar

- **Kenetlenme:** TS 500:2000 §9.1 için nervürlü donatıda temel boy `Lb0 = max(0.12 × fyd/fctd × Ø, 20Ø)`. Konum II çarpanı 1.0, Konum I çarpanı 1.4. Konumu betonlama sırasındaki gerçek yerleşim belirler; sadece üst/alt etiketi yetmez. Örtü, aralık ve diğer madde koşulları ayrıca değerlendirilir. [S1, S2]
- **Kesme/uzatma:** Ln/4, moment ihtiyacının bittiğini veya kenetlenmenin sağlandığını tek başına kanıtlamaz. Teorik kesim sonrası TS 500 §7.3 uzatma şartı ile §9.1 birlikte kontrol edilir. [S1]
- **Bindirme:** §9.2.5 ayrı bir kontroldür; ek oranı, konum, sargı ve şaşırtma şartlarını içerir. Kenetlenme boyunu doğrudan bindirme boyu diye kullanma. [S1]
- **Gönye:** TBDY 7.4.3.1(b)'nin ilgili kiriş–kolon detayında `a ≥ 0.4Lb`, `b ≥ 12Ø`, `a+b ≥ Lb` birlikte aranır. Her temel uç detayı için otomatik `düşey kol = 50Ø` sonucu çıkmaz. Bu detayın temel birleşimine uygulanabilirliği ayrıca belirlenmelidir. [S3]
- **Ürün uygulaması:** ideCAD'in sürekli temel yanıtında montaj için komşu açıklık/4, düz için ilgili kolon yüzünden `max(50Ø,Lb)` tarif ediliyor. Bu ürün açıklamasını tüm ilaveler için genel yönetmelik maddesi olarak yorumlama. [S4]

## 2. Mevcut kodda doğrudan görülenler

Dosya: `ST4_Plan_ID_Ciz/PlanIdDrawingManager.KirisAcilim.cs`.

### Sabit malzeme ve konum katsayıları

`KirisAcilimLbPhiKonumI = 48.44` ve `KirisAcilimLbPhiKonumII = 34.60` (inceleme sırasında satır 85–86). Bunlar C30/B420C varsayımıyla açıklanmış. Projenin gerçek beton/çelik sınıfı farklıysa genel hesap olarak kullanılamaz. Bazı dallar `ust` bayrağıyla konum seçiyor; gerçek betonlama konumunu temsil edip etmediği incelenmeli.

Yalnız bu malzeme kabulü ve başka artırımlar olmadan Ø20 için yaklaşık Konum II 69.2 cm, Konum I 96.9 cm bulunur. Bu örnek bir temel için nihai boy onayı değildir.

### İlave donatının uzaması

`KirisAcilimIlaveYuzdenCm` (satır 147 civarı), temel modunda mesnet genişliğini düşmeden yaklaşık `max(Ln/4,50Ø,Lb)` üretir. Üstteki yorum ise genişliğin düşüldüğü normal kiriş yaklaşımını anlatıyor; yorum ile temel dalı ayrıştırılmalı.

`SurekliTemelIlaveKarsiKes` (satır 2793 civarı), ilave net açıklığı aşar fakat karşı mesnet genişliği +20 cm'yi geçemezse boyu net açıklığa indiriyor. Bu, CK T14 karşılaştırmasıyla açıklanan bir çizim kuralı. Kesme sonrasında gerekli kenetlenmenin kaldığı ayrıca kanıtlanmalı; bu not bu kuralın her durumda hatalı olduğunu iddia etmiyor.

### Uç gönye boyunun geometriye kırpılması

`KirisAcilimGonyeHesap` (satır 2888 civarı):

- Temelde düz giriş dalı atlanıyor; kullanıcının uçta gönye tercihi uygulanıyor.
- Düşey gönye hedefinde 50Ø dikkate alınıyor.
- `b = min(bWant, roomCm)` ile gerçek kol kullanılabilir yüksekliğe indiriliyor.
- Temel dalı, normal kirişteki ikinci kol hesabından önce dönüyor.

Dolayısıyla **hedeflenen boy ile gerçekten çizilen boy farklı olabilir**. Fonksiyonun çağıranları ve varsa `eksik` kontrolü de izlenerek, yetersizliğin son kullanıcıya gerçekten bildirildiği araştırılmalı. Sadece bu fonksiyondan hareketle bütün akışta kontrol bulunmadığı söylenemez.

### Yerel referanstaki ters konum ifadesi

`.cursor/rules/yonetmelik-ve-cizim-kurallari.mdc`, “Kenetlenme ve Bindirme Boyları” bölümünde “Konum II (üst donatı): ×1.4” yazıyor. Konum adları standartla ters. Bu araştırmada dosya değiştirilmedi. Düzeltilecekse koordinasyon kapsamına al; aynı yanlış özeti yeni hesaplara taşıma.

## 3. Önerilen uygulama sırası

1. Gerçek malzeme sınıfının nereden alınacağını ve eksikse mevcut kabulün nasıl belirtileceğini belirle.
2. Donatı rolünü (montaj/düz/ilave/gövde), betonlama konumunu ve çekme/basınç durumunu ayrı ele al. Temelde çekme tarafını yalnız isimden veya normal kat kirişi şablonundan türetme; moment zarfını esas al.
3. Gerekli ankraj, seçilen çizim uzatması, gerçek kullanılabilir boy ve bindirme gereksinimini ayrı değerler olarak tut. Birbirinin yerine kullanma.
4. Geometriye kırpma sonrası yeniden yeterlilik kontrolü yap. Yetersiz bir çizimi sessizce uygun kabul etme; hangi çubukta ne kadar eksik kaldığını göster. Uygun detay çözümü geometri ve ilgili kurallarla seçilsin.
5. STA4CAD ile aynı şekli üretme hedefini ayrı bir karşılaştırma olarak sürdür. Aynı çizimi üretmek yönetmelik kontrolünün yerine geçmesin.

## 4. Değişiklik yapılırsa anlamlı doğrulamalar

- C30/B420C örneği ve farklı bir malzeme sınıfında Lb'nin gerçekten değişmesi.
- Aynı çapta iki betonlama konumu; üst/alt bayrağının tek başına karar vermemesi.
- Gönye sığan ve sığmayan uçlar; gerekli boyun hemen altı/üstü sınır durumları.
- Karşı mesnette kesilen kısa açıklık ilavesi; kesme sonrası yeterlilik.
- Bindirme uygulanıyorsa ek oranı, konum, sargı ve şaşırtma denetimi.
- CK T6/T14/T15/T25 mevcut çizim karşılaştırmaları; normal kat kirişi davranışının korunması.

Değişiklik kapsamına uygun testleri ve `pwsh -NoProfile -File tools/Verify-Project.ps1` çalıştır. Test, ürün derlemesi, DLL kopyası ve canlı AutoCAD denemesini ayrı raporla. Plan ID'nin net48 ürünü ile 2027/net10 test koşucusunu eşdeğer ürün hedefi sayma.

## 5. Kaynaklar ve kanıt sınırı

- **S1 — TS 500:2000 tam metni, üniversite sunucusundaki kopya:** [PDF](https://abs.cu.edu.tr/Dokumanlar/2016/IMZ312/363439620_ts_500_-_betonarme_yapilarin_tasarimi_ve_yapim_kurallari.pdf). İlgili yerler: §7.3, §9.1, §9.2.5, §10.4.3. Erişilen metin Şubat 2000 baskısıdır; sonraki tadillerin tamamını içerdiği bu araştırmada doğrulanmadı.
- **S2 — ideCAD, Donatının Kenetlenmesi TS 500:** [Üretici teknik belgesi](https://help.idecad.com.tr/ideCAD/donatnn-kenetlenmesi-ts-500).
- **S3 — İlgili TBDY kiriş uç detayı:** [Üreticinin madde aktarımı](https://help.idecad.com.tr/ideCAD/kirisin-perde-ile-kenetlenme-boyu-saglanamadi). AFAD PDF bağlantısına erişim başarısız oldu; bu notta o PDF doğrudan okunmuş gibi gösterilmiyor. Nihai uygulamada resmi TBDY metni üzerinden kapsam ve şekil kontrolü tamamlanmalı.
- **S4 — Sürekli temel kirişinde kenetlenme/bindirme:** [ideCAD forumundaki üretici uzman yanıtı, 18.03.2024](https://forums.idecad.com.tr/forum/threads/s%C3%9Crekl%C4%B0-temel-k%C4%B0r%C4%B0%C5%9E%C4%B0-kenetlenme-ve-b%C4%B0nd%C4%B0rme-boyu.16489/). Standart metni değil, ürün uygulaması açıklamasıdır.
- **Proje hafızası:** `docs/kiris-donati-cizim-referansi.md` ve `docs/modul-durum/ST4_Plan_ID_Ciz.SurekliGorunus.md`. STA4CAD örnek eşleşmeleri bu kayıtlardan gelir; bu araştırmada yeniden çizim üretilmedi.

Bu devirde yalnız belge hazırlandı. C# değişikliği, test, derleme, DLL kopyalama veya canlı AutoCAD doğrulaması yapılmadı.
