# Proje çalışma talimatları

## C# / AutoCAD çalışma düzeni

- Kullanıcıyla Türkçe, kısa ve sonuç odaklı iletişim kur. İstenen işi yetki kapsamında tamamla; rutin uygulama kararları için tekrar onay isteme.
- C# veya derleme işlerinde `docs/csharp-autocad-calisma.md` dosyasını oku ve uygula. Mevcut teknik `.cursor/rules/` kurallarını ilgili kapsamda birlikte uygula.
- Öncelik AutoCAD stabilitesi ve mevcut çizim/hesap davranışıdır. Küçük, kanıtlanabilir değişiklik yap; toplu biçimlendirme ve görev dışı refaktör yapma.
- Hedef AutoCAD sürümünü proje ve görev bağlamından belirle. net48 ve AutoCAD 2027/net10 çıktılarını birbirinin yerine sunma; belirsiz sürümde mevcut bağlamı araştır.
- Değişen davranışa uygun testleri çalıştır. Genel doğrulama: `pwsh -NoProfile -File tools/Verify-Project.ps1`. Test, derleme, DLL kopyalama ve gerçek AutoCAD doğrulamasını ayrı raporla.
- İş bittiğinde ne değiştiğini, doğrulamayı, kalan somut kısıtı ve gerekiyorsa tam DLL yolunu yaz. Modül durum kaydını güncel tut.

## Cursor ve Codex ortak çalışma

- Her görevin başında ve düzenlemeye geçmeden önce `docs/ajan-koordinasyon/PROTOKOL.md` dosyasını ve `docs/ajan-koordinasyon/oturumlar/` içindeki aktif kayıtları oku; oradaki koordinasyon protokolünü uygula.
- İlgili işlerde `docs/modul-durum/INDEX.md`, ilgili modül durum dosyası ve `.cursor/rules/` altındaki ilgili teknik kuralları oku.
- Kendi sohbetine özel çalışma kaydı tut; başka ajanın kaydını değiştirme. Aktif başka işin dosyalarına veya ortak derleme/DLL çıktısına çakışan değişiklik yapma.

## Panel simgeleri

Kullanıcının kalıcı tercihi: Yardımcı ve ST4 ID panellerindeki yeni komutlar için mevcut **Monolit** ailesine uygun, komutun işlevini anlatan simgeler üret ve panel eşlemesine ekle.

Görsel dil sıcak fildişi beton, kobalt mavi donatı ve turuncu vurgulu izometrik şekillerdir. Şeffaflık ve 28–32 pikselde okunabilirlik korunmalıdır. Onaylı atlaslar ve uygulama ayrıntıları için `.cursor/rules/betonarme-simge.mdc` dosyasını oku. Önceki ince beyaz/yeşil simge standardı artık varsayılan değildir.
