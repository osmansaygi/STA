# Plan kesitleri — yerleşim ve veri kuralları

## 1. Kesit doğrultuları (önerilen kurallar)

| Kural | Açıklama |
|--------|-----------|
| **K1 — İki kesit** | Her planda **yatay** ve **dikey** kesit çizgisi vardır. **KESİT 1-1** her zaman **yatay çizginin** profili (kutu **planların üstünde**). **KESİT 2-2** her zaman **dikey çizginin** profili (kutu **planların solunda**). |
| **K2 — Hizalama** | Üst kutu X’te plandaki **yatay kesit hattına** (boyunca zincir ortası) göre hizalanır. Sol kutu Y’de **dikey kesit X’ine** göre (boyunca zincir ortası) hizalanır. |
| **K3 — Aks etiketi mesafesi** | Üst kesit kutusu, üst X aks balon/etiket bandının en az **100 cm üstünde**. Sol kesit, sol Y aks etiket alanının en az **100 cm solunda** (kot şemasının sağ kenarı). |
| **K4 — Simetrik bina** | İki kesit birbirine dik ve merkezden geçer; L/U planlarda ileride ağırlık merkezi veya ana blok zarfı ile iyileştirilebilir. |
| **K5 — Şerit genişliği** | Kesitte eleman sayılması için planda **~50 cm** yarıçaplı şerit (kesit düzlemi kalınlığı) kullanılır. |
| **K6 — Planda gösterim** | Planda **yalnızca kesit çizgisi + bakış okları**; profil **KESİT 1-1 / 2-2** kutularında (birebir cm ölçekte). |
| **K7 — Konum** | Üst kutu: plan üstünden en az **~340 cm** veya (hangisi büyükse) aks üst bandı + **100 cm**; sol kutu: aks solundan **100 cm** + kot genişliği (en az ~320 cm plan solu tercihi ile birlikte). |
| **K8 — Kesit hattı** | Dolu şeritten geçme **tercih** (zorunlu değil). Uzun sürekli band hafif tercih. Boyuna kesme (aks paralelliği) ağır ceza. Merdiven kaçınması. |
| **K9 — Hizalama** | Üst kesit: kesit düzlemi ve kot ekseni planla hizalı. **Sol kesit kutusunda kot ekseninde (X) simetri**; zincir (Y) aynalanmaz. |

## 2. Kesitte çizilecek elemanlar (ST4 kotlarıyla)

Kesit şeridiyle kesişen tüm tippler, varsa ilgili kotlarla şemada gösterilir:

- Kalıp planı: **kolon, kiriş, perde, döşeme**
- Temel planı: **sürekli temel, radye (ampatman plakası), bağ kirişi, radye temel temel hatılı (tie/hatıl verisi), tekil temel**, ayrıca **zemin kat perdeleri/kolonları**

Kotlar ST4’teki bina tabanı / kat kotu / eleman kot alanlarından türetilir (tam 3B model yerine kesit düzlemine projeksiyon).

## 3. 4. ve 5. maddeler (önceki mesaj)

- **4**: Antet (pafta başlığı) artık **üst kesit kutusunun üstüne** yerleştirilir; böylece kesit ile plan çakışmaz.
- **5**: Her plan yanında **iki kesit** vardır: **üstte 1-1**, **solda 2-2**; ikisi farklı doğrultuda keser.

Bu doküman `ST4PLANID` kesit mantığıyla uyumludur; kurallar `.cursor/rules/plan-kesit-kurallari.mdc` ile AI tarafından da okunur.
