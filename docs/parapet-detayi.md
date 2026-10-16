# Parapet detayı

Komut: `PARAPET` veya `PARAPETDETAY`. BAPANEL içinde **Parapet Detayı**.
Formdaki **Manuel çiz** düğmesinden sonra parapet sol yüzü ile döşeme üstünün
kesişim noktası seçilir. ESC işlemi iptal eder. Girdiler `parapet.ini` içinde saklanır.

## Referans DWG incelemesi

Kaynak: kullanıcının Desktop/parapet.dwg dosyasının çalışma kopyası.
AutoCAD 2027 Core Console ile ASCII DXF'e dönüştürüldü; model alanındaki
geometri, ölçü ve yazılar okundu.

- Döşeme kenarında, döşeme solda; parapet 20/50 cm, döşeme 15 cm.
- Döşeme üst kotu +3.00, parapet üst kotu +3.50.
- Çizim boyları gerçek cm değerlerinin iki katı; ölçüler 1/25 gösterime uygun.
- Yatay donatı: 2x4Ø10/15. Düşey: Ø12/15.
- Düşey çubukların nominal açılımları: 125+60+15=200 ve 75+60+15=150 cm.
- Geometriye göre paspayı 2.5 cm (çubuk eksenine), üst gönye 15 cm.
- Tarama yok. İki SOLID yalnız kot işaretlerinin üçgenlerinde bulunuyor.
- Güncel kot simgesi temel hatılı standardında, sol üçgeni ACI 250 SOLID taramalıdır.
  Kesitte beton/dolgu taraması yoktur.

## Girdiler ve yerleşim

Döşeme yüksekliği ve kalınlığı kullanıcının açıklaması doğrultusunda tek girdidir.
Parapet yüksekliği döşeme üstünden yukarı ölçülür. Sakal döşeme alt yüzünden
aşağı ölçülür; 0 değeri sarkmasız kesittir. Parapet alt kotu sakallı durumda
`döşeme üst kotu − döşeme kalınlığı − sakal` olur.

Üç yerleşim vardır: döşeme kenarı (döşeme solda), döşeme ortası (iki tarafta
döşeme), kiriş üzeri (parapet sağ dış yüzü kiriş sağ dış yüzüyle hizalı; döşeme solda).
Kiriş yüksekliği üstten alta toplam yüksekliktir; kiriş üstü döşeme üstü ile aynıdır.
Kiriş boyutları yalnız kiriş seçeneğinde etkinleşir. Bu seçenekte sakal 0'a alınır
ve alan pasifleşir; eski kayıtlar da yüklenirken normalleştirilir. Doğrudan
geçersiz kiriş+sakal girdisi çizim doğrulamasından geçmez.

İki düşey çubuğun üst ve alt gönyeleri temel hatılındaki gibi birbirine bakar.
Gövde alt ucu, döşeme/kiriş/sakal alt kotlarının en düşüğünden paspayı yukarıdadır.
Alt gönye temel hatılı hesabıyla otomatik oluşturulur: en az 12 çap ve toplam
nominal boy 5 cm katı. Eski 125/75 cm alt kol girdileri artık kullanılmaz;
eski INI içindeki bu anahtarlar yok sayılır. Ek U donatısı kaldırılmıştır.
20/50 parapet, 15 cm döşeme, 2.5 cm paspayı, Ø12 için gövde 60 cm,
üst/alt gönye 15/15 cm ve L=90 cm; 20 cm döşemede gövde 65 cm ve L=95 cm'dir.

Alt gönye parapet kalınlığına sığmadığında iç yüzdeki çubuğun yatay kolu
kesit genişliği − 2×paspayı mesafesinde yukarı dönen ek gönye eklenir.
Önceki 3 cm içeri alma uygulanmaz. İkinci gönye kendi başına en az 12 çap olur;
tek gönyeli durumda yatay alt kol en az 12 çaptır. Yüz bazındaki toplam
boy 5 cm katına tamamlanır. Ek gönye kesitte, açılımda ve boy etiketinde vardır.
Yuvarlama payı tek gönyelide alt kola, çift gönyelide ikinci kola eklenir.
Sakallı dar kesitte dış çubuk da sığmıyorsa aynı dönüş uygulanır.
Döşeme ortası seçeneğinde alt gönye döşemeye doğru uzar; ikinci gönye yapılmaz.

Parapet donatı kesitleri DONATI katmanında ø1.5 cm, iki yaylı kapalı polyline ve
6 çizim birimi sabit genişlikle çizilir. Merkezler gövdenin 1.5 cm içindedir;
üst sıra üst gönyenin 1.5 cm altındadır. Ara sıralar temel hatılındaki gibi üst sıra
ile döşeme üst donatı izinin altındaki sıra arasında, girilen aralıktan büyük
boşluk kalmayacak şekilde eşit dağıtılır. Döşeme içinde iki yatay iz ve 20 cm adımlı
kesitler DONATI IZI katmanındadır. Döşeme kenarında yatay izler dış kenardan
paspayı kadar içeride iki yuvarlatılmış köşeyle birbirine bağlanır. Kiriş dış kenarı
da aynı döşeme izi dönüşünü kullanır.
Kiriş içindeki iki yatay destek izi kaldırılmıştır. Kirişin tüm yüksekliğini
saran, dört kenarda paspayını koruyan, yuvarlatılmış kapalı etriye ve içe dönen
iki kapatma kancası ETRIYE (BEYKENT) katmanında çizilir. Dört köşedeki boyuna
donatı kesitleri DONATI IZI katmanındadır.

Sakal sıraları alt gönyenin 1.5 cm üstünden başlayıp üst parapette oluşan gerçek
sıra adımıyla yukarı kopyalanır. Üst uca ikinci bir sıra zorlanarak boşluklar
sıkıştırılmaz. Sakal sıraları yatay etiketin 2xN adedine ve metraj tablosuna dahildir.
94 cm parapet / 30 cm sakal / 20 cm aralık / 2.5 cm paspayı örneğinde üstte
5 sıra, sakalda 2 sıra; toplam 2x7 bulunur.

## Toplam uzunluk ve metraj

Toplam parapet boyu cm olarak girilir ve INI'de saklanır. 0 ise düşey etiketlerde
adet öneki bulunmaz ve yatay metraj tablosu çizilmez. Pozitifse her yüz için düşey
adet temel hatılı hesabındaki gibi `tavan(toplam boy / düşey aralık)` olur.

Yatay metraj tek kesintisiz parapet boyu olarak değerlendirilir. Stok 1200 cm,
bindirme temel hatılındaki gibi 60 cm'dir. Her ilave tam boyun net katkısı 1140 cm;
son parça 5 cm katına yukarı yuvarlanır. Aynı boylar gruplanır ve etiketler
`2x[sıra]x[parça adedi]ø[çap] L=[boy] (YATAY)` biçiminde, açılımların sağında
IC ANTET çizgili tabloya yazılır. Sıralara sakal da dahildir.
3400 cm için her sıra/yüzde 2 adet 1200 + 1 adet 1120;
2000 cm için 1 adet 1200 + 1 adet 860 gerekir. Aynı boya denk gelen son parça
tam boy grubuna eklenir; sıfır boylu satır yazılmaz.

DONATIMETRAJ poz basarken parapet ve temel hatılı yatay boy tablolarının ilk
sütununu IC ANTET katmanındaki LINE/polyline sınırlarından tanır. Poz balonu ve
numarasının görünen kutu merkezleri ilgili satırın ilk hücresine ortalanır.
Çok haneli numara hücreye sığmıyorsa yazı ve balon birlikte küçültülür; dolgu
son konumda oluşturulur. Mevcut eski tabloların yeniden çizilmesi gerekmez.
Tablo dışındaki donatı etiketlerinin poz yerleştirmesi korunur.

Kotlar kesitin sağında, açılımlar onların sağında aynı üst/alt kotta yer alır.
Açılımların en yakın noktaları arasında 5 cm boşluk vardır. Düşey etiketler
gövdenin solunda 90°; gönye ölçüleri kancaların üstünde/altındadır.
Parapet başlığı kesit ve açılımların birlikte altına ortalanır; ölçek onun altındadır.
Düşey ölçüler kesitin sağında aynı X üzerinde boşluksuz bir zincirdir:
parapet, döşeme ve varsa döşeme altındaki sakal/kiriş bölümü. Dışındaki ikinci
hat en alt beton yüzünden parapet üstüne kadar tek toplam ölçüyü verir.
İki düşey ölçü hattı arasındaki mesafe gerçek ölçekte 10 cm'dir.
Örneğin 100 cm parapet + 15 cm döşeme + 30 cm sakal için zincir 100/15/30,
toplam ölçü 145 cm'dir. Döşeme kalınlığı solda ayrı ölçülmez.

Manuel kesit, kotlar, gerçek ölçüler, donatı izleri ve iki düşey açılım üretilir.
Plandan tip tarama bu sürümde yoktur.

## Doğrulama

`tools/ParapetSmoke` üretim çizicisini doğrudan derleyerek AutoCAD 2027 motorunda
15/20 cm parapet kalınlığı, 15/20 cm döşeme ve izin verilen 0/25/75 cm sakal
kombinasyonlarını 0/2000/3400 cm toplam boylarla birlikte 84 kesitte sınar;
kiriş+sakal durumlarının reddini ayrıca sınar.
Yalnız kot simgesinde
dolgu bulunması, geçerli entity sınırları, kesit ve iz katmanları, kapalı iki yaylı
kesit polylineleri, 6 birim genişlik, otomatik gönye/boylar, iki düşey açılımın
etiket yönleri, standart kot yazısı ve başlık/ölçek sırası kontrol edilir.
Ek gönye adedi/yönü, kiriş dış yüz hizası ve döşeme kenarı dönüşünün paspayı
mesafesi de AutoCAD geometrisi üzerinden kontrol edilir.
Kapalı etriyenin dört paspayı sınırı/kancaları, ikinci gönye dönüşünün tam
genişlik−2×paspayı konumu ve döşeme ortasında ek gönye bulunmaması da doğrulanır.
Yatay etikette sakal dahil gerçek kesit sayısı, stok satırlarının adet/boyları,
0 uzunlukta tablo yokluğu, düşey adetler ve stok sınır/bindirme hesapları da sınanır.
Düşey zincirin aynı X'te, boşluksuz ve tüm kesiti kapsaması; dış ölçünün tek
parça olarak parapet üstü ile en alt yüz arasını ölçmesi de 84 kesitte doğrulanır.
Poz hücresi testleri mevcut parapet tablolarında ve temel hatılı biçiminde LINE
ızgaralarda çalışır; tek ve dokuz haneli pozlar için yazı/balon merkezleri ve hücre
içinde kalma kontrol edilir. Bu test poz yerleştirme yardımcısını sınar;
DONATIMETRAJ komutunun tüm canlı kullanıcı akışını çalıştırmaz.
Dar kiriş, aşırı paspayı, sıfır aralık ve NaN girdileri reddedilir.
Bu kontrol formun canlı AutoCAD arayüzündeki etkileşim testi değildir.
