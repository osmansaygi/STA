# Performans ve disk temizliği

## ST4PLANID çizim hızı
- **Draw** süresince tek **NetTopologySuite `GeometryFactory`** kullanılır; kolon/kiriş/döşeme birleşimlerinde tekrar tekrar `new GeometryFactory()` atılması kaldırıldı.
- Kolon donatı tablosu (`GetColumnFoundationHeights`, `GetColumnTableExtraData`) kendi fabrikasını üretir; Draw dışında çalışır.
- Kesit şeridi örnekleme (`SampleVerticalOnOccupancy` vb.) statik paylaşımlı fabrika kullanır.

## Eski DLL dosyaları
Her build `ST4_Plan_ID_Ciz_yyyyMMdd_HHmmss.dll` üretir. Disk için:

```powershell
.\scripts\Clean-PlanBinDlls.ps1
```

Varsayılan son **8** DLL kalır; parametre: `-Keep 12`.

## Büyük veri dosyaları (GPR/ST4)
Projede isteğe bağlı `.gitignore` için kök `.gitignore` içinde `*.GPR` / `*.ST4` satırları yorumda; depoya almak istemiyorsanız açabilirsiniz.
