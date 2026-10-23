# Yardımcı ve Plan — form tasarım seçimi

Tarih: 22.10.2026. Güncel kullanıcı seçimi: A / Standart; formlar her zaman açık, düğme ve kutular kullanıcının AutoCAD ekran kesitine yakın yerel Windows görünümünde. Uygulandı ve derlendi; canlı AutoCAD doğrulaması açık. Aşağıdaki Monolit/koyu seçenekler tarihsel kararlardır, güncel form tercihi değildir.

Kullanıcı, her iki programın formlarının sade ve özenli tasarlanmasını, kaynaklar değiştirilmeden önce seçeneklerin gösterilmesini istedi.

## Alternatifler

- A / Açık ve ferah: beyaz yüzey, açık gri alanlar, grafit metin, kobalt ana düğme. Daha rahat aralıklar.
- B / Koyu ve kompakt: antrasit yüzey, açık metin, kobalt vurgu. Çizim alanında az yer kaplayan sıkı yerleşim.
- C / Monolit: sıcak fildişi yüzey, grafit metin, kobalt ana düğme, sınırlı turuncu vurgu. Mevcut Monolit simge ailesiyle bütünlük.

Örnekler gerçek kaynaklardan OtoOlcuForm ve KolonDuseyForm temel alınarak hazırlanır. Aynı içerikle üç tema karşılaştırılır. Oto Ölçü'de Tek ölçü durumu gösterilir; Çift ölçüye ait koşullu alanların kaldırılması önerilmez. Görseldeki katman/şablon değerleri temsili, gerçek çizimden alınmamıştır.

Üretim aracı: yerleşik imagegen, ui-mockup. İstem: üç sütunlu Türkçe karşılaştırma panosu; her sütunda aynı Oto Ölçü ve Kolon Düşey formu; okunaklı Segoe UI, düz yüzeyler, küçük köşe yarıçapı, tutarlı hizalar, yeni özellik eklemeden açık/koyu/Monolit seçenekleri. Görseller çalışma zamanı ekran görüntüsü veya piksel düzeyinde uygulanmış form değildir.

## Uygulama sınırı

İlk renk/simge uygulaması kullanıcı tarafından yetersiz bulundu. Son talimatla 26 form sınıfının tamamında gerçek yerleşim yeniden kuruldu: yatay alanlar, anlamlı ayar sekmeleri, kısa katman listeleri, bir arada çap/aralık ve boy/en alanları, sabit alt işlem satırı. Büyük simgeli başlıklar kaldırıldı. Mevcut kontrol nesneleri ve olayları korunur; hesap ve çizim kaynakları değişmedi. 26 gerçek form sınıfı açık/koyu ve bütün sekmelerde test edildi. Test görüntüleri tools/MonolitFormCheck/preview/ içindedir. Ayrıntı ve güncel NETLOAD teslimi: [ortak form durumu](../modul-durum/ST4_FormTasarimi.md).

Ana modül durum dosyaları diğer aktif oturumların kapsamında olduğundan değiştirilmedi; bu dosya iki modülün tasarım seçim kaydıdır.

## Güncel tercih — açık standart diyaloglar
Kullanıcı 22.10.2026 tarihinde koyu form istemediğini, AutoCAD standart formlarına yakın açık görünüm ve uygulamadan önce seçenekler istediğini belirtti. Önceki otomatik açık/koyu ve Monolit form görünümü kararı geçersizdir; panel simgeleri bu kararın kapsamında değildir.

- A / Klasik: açık gri zemin, beyaz alanlar, standart grup kutuları; bütün temel ayarlar aynı görünümde.
- B / Kompakt: aynı standart kontroller, yan yana ayar ve katman grupları; daha geniş fakat daha kısa pencere.
- C / Sekmeli: Genel ve Katmanlar standart sekmeleri; daha küçük pencere, katman yönetimine sekmeyle geçiş.

Öneri: B; kullanıcı kompaktlık ve kullanım kolaylığı istediğinden ayarları saklamadan pencere yüksekliğini azaltır. Görsel temsili imagegen tasarımıdır, çalışan form ekran görüntüsü değildir. Bu adımda C#/DLL değişmedi. Referans: https://www.autodesk.com/blogs/autocad/autocad-options-dialog-box-part-one/ . Dünyada kullanım sıralaması iddiası yapılmaz.


### Kullanıcı ekran kesitiyle kontrol stili
Son referans, kullanıcının paylaştığı AutoCAD Apply/Cancel/Help düğmeleri ve sayısal giriş kesitidir. Önceki kalın gri/bevel düğmeler yerine beyaza yakın dolgu, ince açık gri kenarlık, küçük köşe yarıçapı ve soluk devre dışı yazısı esas alınır. Girişler beyaz, form açık gri; aynı kontrol stiliyle Standart/Kompakt/Sekmeli seçenekleri sunulur. Görseller taslaktır; uygulama seçimi beklenir.


### A seçildi ve uygulandı
Kullanıcı A seçeneğine göre derleme ve tüm kot kutularının cm olmasını istedi. 26 formda standart açık görünüm uygulandı. Oto Donatı ve Sehpa Donatı metre girişleri cm yapıldı; kayıt/hesap metre alanları dönüşümle korunur. Güncel iki DLL 20261022_150054; 26 form testi ve iki net48 derleme başarılı. Ayrıntı ortak form durumunda.

