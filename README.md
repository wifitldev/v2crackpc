# v2crackN

**Десктопный (Windows) клиент-прокси** — форк [2dust/v2rayN](https://github.com/2dust/v2rayN)
с доработками из мобильного проекта **v2crackNG**.

[![Release](https://img.shields.io/github/v/release/wifitldev/v2crackpc?logo=github&label=Release)](https://github.com/wifitldev/v2crackpc/releases)
[![Downloads](https://img.shields.io/github/downloads/wifitldev/v2crackpc/latest/total?logo=github&label=Downloads)](https://github.com/wifitldev/v2crackpc/releases)
[![Windows](https://img.shields.io/badge/Windows-supported-0078D6?logo=windows)](https://github.com/wifitldev/v2crackpc)
[![License](https://img.shields.io/badge/license-GPL--3.0-green)](LICENSE)

> A Windows GUI client for Xray / sing-box, forked from v2rayN.

---

## Скачать

**https://github.com/wifitldev/v2crackpc/releases**

Архив `v2crackN-windows-64.zip` — самодостаточный, **.NET устанавливать не нужно**.

### Установка
1. Распаковать архив в любую папку (например `C:\Programs\v2crackN`).
2. Запустить `v2crackN.exe`.

Ядро (Xray, sing-box), `geoip.dat`, `geosite.dat` и `wintun.dll` **уже внутри** — докачивать ничего не надо.

---

## Что отличается от обычного v2rayN

| Фича | Описание |
| --- | --- |
| **Брендирование** | Имя `v2crackN`, свой EXE, заголовки окон, папка данных, свои иконки |
| **Свои обновления** | Обновления берутся из этого репозитория (`Global.AppRepo = "wifitldev/v2crackpc"`), а не с 2dust/v2rayN |
| **Принудительная подписка** | Создаётся при **каждом** старте, **удалить и переименовать нельзя**, обновляется автоматически через 3 с после запуска |
| **Проверка версии** | Код проверки на собственном бэкенде есть (`BackendVersionCheckService`), но **выключен**: `Global.AppVersionCheckUrl = ""` |
| **Иконки** | Свои иконки приложения и значки в трее + генератор `tools/IconGen` |
| **Пресеты RU/IR** | `Settings → Regional Presets → Default / Russia / Iran` — это **уже было** в v2rayN, ничего не менялось |

Полное описание всех правок: **[ЧТО_ИЗМЕНЕНО.md](ЧТО_ИЗМЕНЕНО.md)**

### Постоянная подписка

Задаётся константами в [`v2rayN/ServiceLib/Global.cs`](v2rayN/ServiceLib/Global.cs):

```csharp
PermanentSubId   = "permanent_v2crackn";
PermanentSubUrl  = "...";   // адрес вашей подписки
```

Логика: `ConfigHandler.EnsurePermanentSubscription()` вызывается из `AppManager.InitApp()`
и каждый раз возвращает подписку к исходному состоянию — её нельзя удалить (`DeleteSubItem`
возвращает `-1`), переименовать или перенаправить на другой URL.

---

## Сборка из исходников

Требуется **.NET SDK 10**.

```powershell
git clone https://github.com/wifitldev/v2crackpc.git
cd v2crackpc

dotnet build v2rayN\v2rayN.sln -c Release
dotnet test  v2rayN\v2rayN.sln -c Release --no-build   # 125 тестов
```

| Проект | Назначение |
| --- | --- |
| `v2rayN\v2rayN` | WPF — основная сборка под Windows |
| `v2rayN\v2rayN.Desktop` | Avalonia — кроссплатформенная (Linux / macOS) |

> [!NOTE]
> В исходниках **нет бинарных ядер** — как и в апстриме. Для готового релиза
> ядра подкладываются скриптом упаковки (см. ниже) или скачиваются в меню
> **Help → Check Update**.

---

## Как выпустить новую версию

```powershell
# 1. поднять версию в v2rayN\Directory.Build.props (например 1.0.2)
# 2. собрать самодостаточный архив с ядрами
powershell -File tools\pack.ps1
# 3. закоммитить и запушить
git add -A
git commit -m "release 1.0.2"
git push

# 4. GitHub → Releases → Create a new release
#    тег:      1.0.2
#    ассет:    dist\v2crackN-windows-64.zip
```

Имя ассета **должно** совпадать с `Global.AppReleaseAssetName`:

```
v2crackN-windows-64.zip
v2crackN-windows-arm64.zip
v2crackN-linux-64.zip
v2crackN-linux-arm64.zip
v2crackN-macos-64.zip
v2crackN-macos-arm64.zip
```

Как только релиз опубликован, клиент сам подхватит его:
**Help → Check Update** сверяет тег релиза с текущей версией.

`tools\pack.ps1` принимает параметры, если ядра лежат не там:

```powershell
.\tools\pack.ps1 -CoreSource "C:\папка\с\ядром" -SingBoxExe "C:\path\sing-box.exe"
```

---

## Структура репозитория

```
v2crackpc/
├── v2rayN/
│   ├── ServiceLib/          # общая логика: модели, хендлеры, вью-модели, сервисы
│   ├── v2rayN/              # WPF-приложение (Windows)
│   └── v2rayN.Desktop/      # Avalonia-приложение (Windows/Linux/macOS)
├── tools/
│   ├── IconGen/             # генератор иконок (dotnet run --project tools\IconGen)
│   └── pack.ps1             # сборка релизного zip с ядрами
├── _icon/                   # исходник иконки + превью
├── ЧТО_ИЗМЕНЕНО.md          # описание всех отличий от v2rayN
└── README.md
```

Точки входа, куда править при брендинге или настройках:

| Что | Где |
| --- | --- |
| Имя приложения, репо обновлений, вечная подписка | `v2rayN/ServiceLib/Global.cs` |
| Ссылки на скачивание ядер и апдейтов | `v2rayN/ServiceLib/Manager/CoreInfoManager.cs` |
| Логика вечной подписки | `v2rayN/ServiceLib/Handler/ConfigHandler.cs` |
| Проверка версии на бэкенде | `v2rayN/ServiceLib/Services/BackendVersionCheckService.cs` |
| Версия сборки | `v2rayN/Directory.Build.props` |

---

## Поддерживаемые ядра

[Xray](https://github.com/XTLS/Xray-core) · [sing-box](https://github.com/SagerNet/sing-box) ·
[mihomo](https://github.com/MetaCubeX/mihomo) · v2fly · hysteria и [другие](https://github.com/2dust/v2rayN/wiki/List-of-supported-cores)

---

## Что пока не сделано

- Сборки под **Linux** и **macOS** — код для них есть (`v2rayN.Desktop`), но не собирался и не тестировался
- Релизы подписываются только загрузкой через GitHub, **GPG-подписи нет** (в отличие от апстрима)
- Своего бэкенда для принудительной проверки версии нет — механизм выключен

---

## Лицензия и авторство

[GPL-3.0](LICENSE).

Форк проекта **[2dust/v2rayN](https://github.com/2dust/v2rayN)** — большое спасибо авторам
за клиент, на котором всё это построено. Мобильный аналог — **[2dust/v2rayNG](https://github.com/2dust/v2rayNG)**.
