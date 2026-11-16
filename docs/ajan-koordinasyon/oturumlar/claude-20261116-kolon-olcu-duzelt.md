# claude-20261116-kolon-olcu-duzelt

- Ajan: Claude (Claude Code)
- Görev: Yardımcı'ya KOLONOLCUDUZELT — kolon aplikasyon planında akslar kaydırılınca kolon kaçıklık ölçülerini yeni akslara göre yeniden kurmak (aks kolon dışına çıkarsa yüzden aksa ölçü).
- Güncelleme: 16.11.2026 17:15 (Europe/Istanbul)
- Durum: tamamlandı — rezervasyon yok
- Kontrol: protokol + son 2 gün kayıtları (aktif yok). Görev başı GUNCEL_2027 = Plan 20261115_222902 + Yardımcı 20261115_150529; Yardımcı kaynağında o yayından sonra değişiklik yoktu. ST4_GUNCEL_2027.bundle yerinde.
- Değişen: yeni ST4_Yardimci/KolonOlcuDuzeltRunner.cs, ST4_Yardimci/Icons/ba_kolonolcuduzelt.png; ST4_Yardimci/CommandEntry.cs, BaHomeRibbonCatalog.cs (Ölçü grubu), BaCommandIcons.cs; docs/modul-durum/ST4_Yardimci.md; tools/KolonOlcuDuzelt20261116/** (Build, Run, RunCk, png, Publish51, Native sürücü KO_DRAW/KO_SHIFT/KO_CHECK/KO_DIMS/KO_PNG, Ikon).
- Doğrulama: izole Yardımcı 2027 0 hata; net48 derleyici hatası yok. Core Console: taze aplikasyonda HHO_01/BYS_T9/N_ULK_02/CK_02 tüm yüzler zaten doğru (Plan ID kuralıyla birebir, CK eğik akslar dahil); aks kaydırma sonrası 46/7/27/127 yüz düzeltildi; tekrar çalıştırma 0 değişiklik. 1/100 HHO aynı.
- Yayın: GUNCEL_2027 = Yardımcı 20261116_171202 + Plan 20261115_222902 (PASS), arşiv NETLOAD/arsiv/GUNCEL_2027_20261116_171222_545. Commit yok. Canlı AutoCAD denenmedi.
- Açık: dairesel kolonlar kapsam dışı; aks ölçüleri (AKS OLCU) güncellenmez.
