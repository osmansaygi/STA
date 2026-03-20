# Döşeme etiketi – veri ve format özeti

## Resimdeki format (hedef)
- **Çerçeve:** 2 yay + 2 düz çizgi (yeşil çerçeve)
- **Ana etiket (12 cm yazı):** `D` + Kat ID + `-` + Döşeme no + ` d=` + kalınlık + `cm`  
  Örnek: `DB-01 d=15cm`
- **Alt satır – hizalama hattına göre sola hizalı:**
  - `Q=5kN/m²` (hareketli yük)
  - `+0.20` (üst kot)
  - `+0.05` (alt kot)
- Döşeme numarası: kata göre 2 veya 3 hane (01, 001 vb.), kolon/kiriş ile aynı mantık.

---

## ST4 Floors Data – mevcut parse

Şu an parser sadece şunları okuyor:
- `p[0]` = slabId
- `p[8..11]` = axis1, axis2, axis3, axis4
- `p[24]` = merdiven bayrağı ("1")

**Örnek satır (GI_A_01):**
```
101,12,.47,.35,0,0,0,0,1002,1004,2001,2002,...
```
- **p[1] = 12** → büyük olasılıkla **döşeme kalınlığı (cm)** → `d=12cm`
- **p[2] = .47** → muhtemelen sabit yük (birim net değil)
- **p[3] = .35** → muhtemelen hareketli yük; resimde Q=5 kN/m² → birim (kN/m² mi, faktör mü?) birlikte netleştirilebilir.

---

## Datada net olmayan / eksik

| Alan            | Resimdeki örnek | Floors Data’da aday | Durum |
|-----------------|------------------|----------------------|--------|
| Kalınlık (d)    | d=15cm           | p[1] = 12, 15, 20    | **Muhtemelen p[1]** (cm) – parse edilebilir. |
| Hareketli yük Q | Q=5kN/m²         | p[3] = .35, .5, .75  | Birim belirsiz (0.35→3.5? 0.5→5? 10 ile çarpım?). **Birlikte bakalım.** |
| Üst kot         | +0.20            | —                    | Bu satırda yok; kat kotu veya başka bölümde olabilir. **Birlikte bakalım.** |
| Alt kot         | +0.05            | —                    | Aynı şekilde. **Birlikte bakalım.** |

---

## Sonraki adım
1. **Kesin verilenler:** `D` sabit, Kat ID (Story ShortName), döşeme no (haneli), çerçeve (2 yay + 2 düz).
2. **Parse’a eklenecek:** `SlabInfo.ThicknessCm` = p[1] (ve istenirse p[2]/p[3] ham olarak da alınabilir).
3. **Q ve kotlar:** ST4’te hangi sütun/satır veya bölümde olduğunu tespit ettikten sonra parser ve etiket metni birlikte güncellenir.

Bu dosyayı referans alarak datada bulamadığımız kısımlara (Q birimi, üst/alt kot nerede) birlikte bakabiliriz.
