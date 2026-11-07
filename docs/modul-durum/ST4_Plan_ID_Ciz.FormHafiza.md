# Plan formlarında kalıcı seçim hafızası

- Tarih: 28.10.2026, Codex.
- Kaynak hazır ve test edildi: kiriş açılımı, sürekli temel açılımı, kolon aplikasyon, temel ilave, kolon düşey, kat sınırı, kapama perde (7 form). Onaylanan seçimler AutoCAD yeniden açıldığında geri yüklenir. İptal kayıtları değiştirmez.
- Kayıt: kullanıcı LocalAppData/ST4_Plan_ID_Ciz altında sürümden bağımsız INI; PlanFormSettings.cs atomik kayıt, yedek kurtarma ve tür/sınır kontrolü sağlar. Eski kolon/kapama kayıtları korunur.
- Doğrulama: tools/PlanFormHafiza20261028/README.md. İzole Release net10/2027 0 hata/4 bilinen uyarı; 3 ayrı Core Console sürecinde tüm formlar yaz/yeniden aç/iptal/eski kayıt/bozuk değer/kayıp kat/yedek kurtarma PASS. Kullanıcının canlı AutoCAD oturumunda denenmedi. Genel Verify-Project mevcut PlanLisans fixture eksikliğiyle durdu.
- Yayın henüz yok: ana modül durum dosyası ve NETLOAD/GUNCEL_2027 Claude radye kaydında ayrılmış; çakışma nedeniyle yazılmadı. İzole doğrulanan DLL: tools/PlanFormHafiza20261028/build/ST4_Plan_ID_Ciz_2027_20261028_193801.dll.
- Kullanıcı yayın sorumlusu olarak Claude'u seçti. Yayın devir notu: docs/ajan-koordinasyon/oturumlar/codex-20261028-plan-form-hafiza-7a772b71.md (Claude'a yayın devri bölümü). Form kaynakları ve testler tamam/serbest; Claude kendi radye değişikliğiyle birlikte son Plan DLL'sini yayımlayacak. Bu sohbet NETLOAD veya ana durum kaydına yazmadı.
