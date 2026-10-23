# ST4 Plan ID — simge durum eki

Güncelleme: 2026-10-19, Codex.

- **Yeniden derleme ve dağıtım (kullanıcının açık talimatıyla):** Son DLL `NETLOAD/ST4_Plan_ID_Ciz_20261019_233438.dll` (Debug/net48). Yeni Monolit PNG doğrudan gömülü; aynı DLL `ST4_Plan_ID_Ciz/bin/Debug/net48` yolunda. Derleme 0 hata/0 uyarı; bin/NETLOAD hash aynı, gömülü PNG hash kaynakla aynı; dağıtılan DLL'de simge yükleyicisi PASS, Verify-Project dört regresyon PASS. `NETLOAD` ve `bin` altındaki 5 eski Plan DLL/PDB silindi (005117 ve 010206); NETLOAD'da yalnız yeni Plan sürümü kaldı. `tools/SurekliTemelIconCheck/deploy-cleanup.txt` kayıtları saklar. Canlı AutoCAD'e DLL yüklenmedi; daha önce yüklenmiş eski assembly diskten silmekle bellekten kalkmaz, yeni DLL temiz oturumda yüklenmeli. Aşağıdaki izole dağıtım notları önceki aşamaların geçmişidir; modül ana durum dosyasındaki 010206 yolu da bu yeni dağıtımla geçersizdir.

- **Panelde görünmeme takibi:** yeni kaynak yalnız izole DLL'de kalmış, NETLOAD eski DLL'leri içeriyordu. Mevcut eski DLL'nin desteklediği komuta özel kullanıcı simgesi `C:/Users/BEYKENT/AppData/Local/ST4Icons/SUREKLITEMELACILIMLARI.png` yoluna kuruldu. Önceden o komut için kişisel dosya yoktu. Bu dosya kaynak yükleyicisinden alınan 32 px Monolit çıktısıdır.
- Eski `NETLOAD/ST4_Plan_ID_Ciz_20261019_010206.dll` içindeki gerçek `StaToolIcons.ForCommand` çağrısı gerçek kullanıcı bağlamında kurulan dosyayı okudu; Fit32 sonrası bütün pikseller eşit (PASS: `tools/SurekliTemelIconCheck/override-validation.txt`). Açık panel resimleri önbellekte olduğundan sağ tık → Simge tasarımı → Monolit seçimiyle yenilenir; aynı oturumda STAPANEL'i gizleyip açmak kontrolü yeniden yaratmaz. Canlı UI tıklaması yapılmadı, DLL yüklenmedi/kopyalanmadı; eklenti yeniden derlenmedi (yalnız doğrulama aracı derlendi).

- `SUREKLITEMELACILIMLARI` simgesi Monolit atlası referans alınarak built-in ImageGen ile yenilendi: fildişi beton şerit temel, kobalt donatı ve turuncu açılım vurgusu; şeffaf PNG.
- Kaynak: `ST4_Plan_ID_Ciz/Icons/surekli_temel_acilim_monolit.png`. Mevcut ayrı gömülü kaynak eşlemesi kullanılır. Atlas sırası, kişisel simge önceliği, Monolit/Klasik seçimi ve çizim kodu değişmedi.
- `tools/SurekliTemelIconCheck/preview.png`: gerçek DLL yükleyicisinden mevcut iki komutla birlikte 28/32 px karşılaştırma; görünüm kontrol edildi. Alpha/içerik, komut eşlemesi ve Klasik seçimi kontrolü geçti.
- `tools/Verify-Project.ps1`: dört mevcut regresyon geçti.
- Debug/net48 derlemesi başarılı: 0 hata, 0 uyarı. İzole çıktı: `tools/SurekliTemelIconCheck/build/ST4_Plan_ID_Ciz_20261019_144744.dll`.
- Ortak `bin/obj/NETLOAD` başka aktif kayıtta ayrıldığı için kullanılmadı; NETLOAD'a DLL kopyalanmadı. AutoCAD içine yükleme ve canlı panel doğrulaması yapılmadı. Bu çıktı AutoCAD 2027/net10 hedefi değildir.
- Önceki simge: `tools/SurekliTemelIconCheck/previous-icon.png`. Üretim istemi: `tools/SurekliTemelIconCheck/prompt.txt`.

Ana modül durum dosyası başka aktif kayıtta ayrıldığı için bu ayrı durum eki kullanıldı; indeks üzerinden erişilebilir.
