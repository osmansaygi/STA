# Plan formları — kalıcı seçim hafızası

28.10.2026, AutoCAD 2027 / net10.0-windows.

Kapsam: Kiriş Açılımları, Sürekli Temel Açılımları, Kolon Aplikasyon Planı, Temel İlave, Kolon Düşey, Kolon Düşey Kat Sınırı ve Kapama Perde.

Çiz/Tamam ile onaylanan tüm seçimler kullanıcıya özel LocalAppData/ST4_Plan_ID_Ciz altında saklanır. İptal ve pencereyi kapatma diskteki onaylı seçimi değiştirmez. Dosyalar DLL sürüm adına bağlı değildir. Kiriş yatay/düşey boşlukları ve paspayı, antet/kat/gönye; sürekli temel birleştirme; kolon aplikasyon dört seçeneği; temel ilave üç sayısal alan; kolon düşey ölçek/kat modu/sınır/son kat; kapama hatıl durumu kapsamda.

Yeni PlanFormSettings: tür ve sınır kontrolü, değişmez sayı biçimi, metin kaçışları, atomik geçici dosya + File.Replace + .bak. Mevcut kolondusey.ini ve kapamaperde.ini adları/anahtarları korunur; önceki düz metin kayıtları okunur. Bozuk/sınır dışı sayılar varsayılana döner; bütünü bozulmuş kayıtta .bak denenir. Projede artık bulunmayan kat için geçerli kat seçilir. Dosya seçim diyaloğundaki mevcut RememberedFilePrompt kalıcılığı korunur. Yardımcı üzerinden yürüyen döşeme/hatıl komutlarının kendi ayar kayıtları korunur.

Doğrulama: ayrı Core Console 2027 süreçlerinde Native/Probe.cs ve Run.ps1, write → read → corrupt. Kullanıcı ayarlarına dokunmadan tools/PlanFormHafiza20261028/settings-test altında. Yazım en-US, yeni süreçte okuma tr-TR. Yedi formun tüm alanları geri yüklendi; hepsinde İptal dosya hashini korudu. Eski kolon/kapama kaydı, kayıp kat, NaN/sınır dışı değer, sıfır dolu dosyadan yedek kurtarma PASS. Tüm süreçler COMPLETE, exit=0.

İzole Release derleme 0 hata / 4 bilinen uyarı. Build: ST4_Plan_ID_Ciz_2027_20261028_193801.dll. Genel Verify-Project, görevden önce de bulunan Test-StaStartup fixture'ında eksik PlanLisans nedeniyle durdu. Ana AutoCAD/çizim/hesap kaynaklarında değişiklik yok; DLL henüz GUNCEL_2027'ye yayımlanmadı (Claude radye işi bu kaynağı ayırmış durumda).
