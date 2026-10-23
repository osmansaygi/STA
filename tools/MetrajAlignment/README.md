# Donatı / hasır poz balonu hizası regresyonu

`Build.ps1 -OutputName fixed-final` eklentiyi Debug/net48 olarak izole üretir.
`Isolated.targets` ortak bin/obj kaynaklarının yanlışlıkla derlenmesini ve
NETLOAD/bundle/setup payload kopyalarını kapatır.

`Native/AlignmentCheck.cs` AutoCAD 2027 Core Console için net10 test koşucusudur.
`ALIGN_PLUGIN` test edilecek eklentinin tam DLL yolu, `ALIGN_RESULT` yeni sonuç
dosyasının tam yolu olmalıdır. Native.csproj derlendikten sonra `run.scr` tam
yoluyla Core Console'a verilir. Çalışan kullanıcı çizimi kullanılmaz.

Test: DBText dört hizalama (dar width factor dahil), MText dört attachment,
0/30/90/180/270 derece; donatı ve hasır; renkli ve renksiz. Poz sayısı, balonun
yazının görsel merkez hattında olması ve numaranın balonda ortalanması kontrol
edilir. Sonuçlar commit sonrası gerçek GeometricExtents üzerinden okunur.

Kanıt: `before-result.txt` 324 kontrolde 280 hata; `final-result.txt` 324
kontrolde 0 hata ve COMPLETE. `fixed-result.txt` ara düzeltmedir: balon hizası
geçmiş, rakamın font kaynaklı merkez farkı henüz giderilmemiştir.

Teslim net48 DLL'si: build/fixed-final/ST4_Yardimci_20261020_151705.dll.
Core Console 2027 kullanılması teslimin 2027 uyumluluğunu doğrulamaz.

Takip: Kullanıcı hem hasır hem diğer pozlarda hatanın sürdüğünü bildirdi.
Koşucu normal ve hızlı dört yerleştirme yolunu artık birlikte test eder.
`all-paths-151705-result.txt`: mevcut 151705 DLL'de 320 etiket, 648 kontrol, 0 hata.
Bu yalnız sentetik doğrulamadır. Gerçek DWG ve kullanılan DLL soruldu; kullanıcı
sorunu henüz yeniden üretilemedi ve çözülmüş sayılmıyor. Bu takipte eklenti
kaynağı değiştirilmedi, yeni teslim DLL'si üretilmedi.

Gerçek çizim kanıtı (kaynak salt okunur):
- CZM_RPR/N_ULK_SU.dwg: 709 donatı ve 46 hasır etiketi.
- real-235451-result.txt: eski DLL ile 755 balonun 755'inde kayma üretildi.
- real-151705-result.txt: teslim klasöründeki güncel DLL ile 755 balonun tümü PASS.
- real-source-before.sha256: orijinal DWG işlem sonunda aynı SHA256 değerinde.
- RealDrawingCheck.cs hiçbir SaveAs çağırmaz; eski poz silme ve yeni yerleşim
  yalnız geçici bellekteki DB'dedir. Son kontrol balonun kaynak etikete göre
  normal doğrultu hizasıdır; bu özel gerçek çizim testinde poz numarası merkez
  kontrolü yapılmaz (numara kontrolü sentetik AlignmentCheck kapsamındadır).
- Teslim: NETLOAD_METRAJ_HIZA/ST4_Yardimci_20261020_151705.dll, bağımlılıklarıyla.

Son kullanıcı kuralı: balon/numara etiketle aynı açı ve orta hatta; etiketin
kendi doğrultusunda solunda kenardan kenara 5.5 cm. Bu ölçüm dünya AABB'sinin
izdüşümüyle değil döndürülmüş geçici klonun yerel sınırıyla doğrulanır.
- gap55-result.txt: dört yol, 320 etiket, 1448 PASS. Yeni kontroller: yerel
  kenar boşluğu, poz numarası açısı, renkli dolgu merkezi.
- real-gap55-result.txt: 709 donatı + 46 hasır, 755/755 hiza ve boşluk PASS.
- Teslim: NETLOAD_METRAJ_HIZA/ST4_Yardimci_20261020_161449.dll, net48.
  151705 artık NETLOAD_METRAJ_HIZA/onceki/ altında arşivdir.

Antet tam akış: AntetFullCheck.cs, CHECK_ANTET_FULL komutu. Scriptte _ALL ile
SheetView adayları seçilir; gerçek RunByAntet tüm tablo/temizlik/yerleşim
adımlarını çalıştırır, sonra Editor.Regen çağrılır. Orijinal DWG yerine
antet-input.dwg çalışma kopyası açılır ve kaydedilmez.
antet-regen-161449-result.txt: 755 balon, 755 numara, 755 dolgu; 0 hata;
yeniden basılan etiketlere ait 0 eski balon kalıntısı; COMPLETE.
Antet seç mevcut 161449 DLL'de zaten aynı yerel 5.5 cm yerleşim kuralını çağırır.
Bu takipte uygulama kodu veya teslim DLL'si değiştirilmedi.
