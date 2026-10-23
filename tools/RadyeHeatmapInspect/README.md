# PDF/DXF almadan radye As alanı

Mevcut GPR dosyası düğüm tasarım As değerlerini içerir. `read_gpr_heatmap.py` RAD koordinatları/elemanları ve SO1 genel/tepe As kontrolüyle bu tabloyu okur. PDF/DXF veya çalışan STA gerekmez. GPR sütunları X alt/X üst/Y alt/Y üst, SO1 sütunları X üst/X alt/Y üst/Y alt sırasındadır; okuyucu bu farkı açıkça eşler.

N_ULK_02: 733 düğüm, 488 eleman, 516 plak köşesi; dokuz STA ekran örneğinde18 As eşleşmesi; dört tepe eşleşmesi. GPR koordinatları 0.001m (max fark0.000471m), As0.01cm²/m hassasiyetindedir. RAD koordinatları kullanılır. Dört ilave tepe0/3.14/0.53/1.45cm²/m. 217 plak bağlantısız düğüm haritaya katılmaz.

`gpr-check/recorded-nodal-As.csv` bütün düğümleri ve bağlantı maskesini, NPZ aynı sayısal veriyi içerir. `gpr-heatmap-validation.json` eşleşme kanıtı ve SHA256'ları verir. `gpr-ilave-isi-haritasi.png` enterpolasyon önizlemesidir. Mevcut radson.pdf yalnız karşılaştırma için ayrıca render edilmiştir; hiçbir okuyucu/harita üreticisi PDF okumaz.

`test_gpr_heatmap.py` yalnız üç kaynak dosyalı geçici klasörden çalışmayı, eksik GPR, koordinat, düğüm, tepe, proje ve donatı açısı hatalarının reddini sınar: yedi PASS.

`export_gpr_heatmap_dxf.py` araştırma çıktısı üretir; kullanıcıdan DXF istenmez. R12 DXF'nin tüm11229 nesnesi Core Console'da açılmış, AUDIT0 hata ile cm birimli DWG'ye kaydedilmiştir. Esas teslim bundan ayrı, yeni otomatik çizim komutudur: `../RadyeAutomaticCheck/README.md`.

Yalnız SO1 kuvvetlerinin düğüme toparlanması tamamlanmadı. Önceki `probe_nodal.py` ve INCOMPLETE_RECOVERY raporu bu araştırmaya aittir; GPR As okumasının yerine kullanılmaz. GPR'nin her yeni hesapta mutlaka bulunduğu varsayılmaz; yoksa otomatik komut açık hata verir. Yeni mühendislik As hesabı yapılmaz.
