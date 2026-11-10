# legacy — архив старых наработок

Эта папка содержит **устаревший** Windows-интерфейс и его упаковку. В релизы она не входит и не собирается.

| Путь | Что это |
| --- | --- |
| `v2rayN/` | Старый WPF-интерфейс Windows-клиента (замена актуальному WPF UI v2rayN). Заменён на [`../app`](../app) |
| `pack.ps1` | Упаковщик старого UI в релизный ZIP. Заменён на [`../tools/pack-ui-proto.ps1`](../tools/pack-ui-proto.ps1) |
| `setup-payload.ps1` | Старый подготовщик payload для нового UI (брал ядра из bin старого UI). Заменён параметром `-CoreSource` в `pack-ui-proto.ps1` |

Актуальный интерфейс: [`../app`](../app) · Кроссплатформенный (Linux/macOS): [`../v2rayN/v2rayN.Desktop`](../v2rayN/v2rayN.Desktop)
