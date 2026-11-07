# Claude — merdiven B-B / poligon kolon mesnet

- Başlangıç: 2026-11-07 22:30
- Durum: tamamlandı — rezervasyon yok
- Görev: MERDIVENCIZ — poligon kolonla çevrili merdivende orta kol (B-B) kesitinde uç mesnet/sahanlık yok; poligon kolon sınırı kesitte görünmüyor.
- Hedef dosya: ST4_Yardimci/MerdivenRunner.cs (TryBoundaryOnAxis)
- Neden: TryBoundaryOnAxis kapalı poligon kolonun tüm kenarlarını birlikte alıyordu; eksene paralel yan kenar (lat≤45) ve karşı uçtaki kenarlar yüzünden tMin<2 / tMax>-2 olup mesnet tümden eleniyordu → orta kolda sahanlık ve kolon görünmüyordu.
- Düzeltme: paralel kenar yok sayılır, yalnız istenen yöndeki kesişimler; kalınlık = yakın yüz → sonraki yüz.
- Derleme: izole (scratchpad) Yardımcı 2027 0/0. Yayın GUNCEL_2027 = Yardımcı ST4_Yardimci_2027_20261107_222019 + Plan 20261107_220221 (değişmedi), arşiv NETLOAD/arsiv/GUNCEL_2027_20261107_222052_487. AutoCAD denemesi yok. Commit yok.
- Ek (22:25): C-C kesitinde sağ kiriş son rıhta çok yakın → bel üst sahanlık alt kotuna kirişten önce ulaşamıyor, alt çizgi landHi atlanıp plak içinden geçiyordu. DrawUniformFlightKesiti: alt çizgi bel doğrultusunda mesnet yüzünde biter (ySagJoin; sol için simetrik ySolJoin), kiriş iç yüzü/kolon finişi bu kota göre. Yayın GUNCEL_2027 = Yardımcı ST4_Yardimci_2027_20261107_222458 + Plan 220221, arşiv NETLOAD/arsiv/GUNCEL_2027_20261107_222522_183. AutoCAD denemesi yok. Durum: tamamlandı — rezervasyon yok.
- Ek (22:29): C-C alt donatı (8) kiriş dış yüzünü aşıp geri kanca yapıyordu (basamak alt ∩ üst sahanlık donatı kotu mesnet ötesinde). 3, 4, 8, 9 donatıları: kesişim mesnet ötesindeyse çubuk eğik doğrultuda mesnet içinde (dış yüz − pas) biter, oradan aşağı kanca. Yayın GUNCEL_2027 = Yardımcı ST4_Yardimci_2027_20261107_222925 + Plan 220221, arşiv NETLOAD/arsiv/GUNCEL_2027_20261107_222936_629. AutoCAD denemesi yok. Durum: tamamlandı — rezervasyon yok.
