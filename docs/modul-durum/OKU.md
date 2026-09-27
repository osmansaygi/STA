# Modül durum dosyaları — nasıl kullanılır

Amaç: “önceden şunu yapmıştık” diye sohbet aratmadan kaldığımız yeri 30 saniyede bulmak.

## Sen (kullanıcı)

1. Yeni sohbete başlarken **modülü yaz** (ör. `ST4_Yardimci döşeme`).
2. “Devam” demek yerine: `docs/modul-durum/ST4_Yardimci.md` + kısa sorun (1–2 cümle / 1 resim).
3. Anlamlı bir düzeltme bittikten sonra agent’tan iste: **“DURUM güncelle”**.

## Agent

1. İlgili `docs/modul-durum/<Modül>.md` dosyasını **önce** oku.
2. Transcript / eski sohbet araması yalnız DURUM yetersizse.
3. İş bitince DURUM’u 3–8 satır güncelle (son DLL, ne düzeldi, sıradaki açık madde).

## Ne yazılır / yazılmaz

| Yaz | Yazma |
|-----|--------|
| Son çalışan DLL yolu | Uzun sohbet özeti |
| Son bilinen hata / kural | Kod kopyası |
| Açık maddeler (max ~5) | Her build geçmişi |
| Kritik kararlar (tek satır) | Screenshot dump |

Klasördeki dosyalar: `ST4_Yardimci.md`, `ST4_Plan_ID_Ciz.md`, `INDEX.md`.
