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

- `v2crackN-<версия>-windows-10-11.zip` — распаковать и запустить `v2crackN.exe`;
- `v2crackN-<версия>-windows-10-11-installer.exe` — установщик в `%LocalAppData%\Programs\v2crackN`.

Оба варианта самодостаточные: **.NET устанавливать не нужно**. Ядро (Xray, sing-box),
`geoip.dat`, `geosite.dat`, `wintun.dll` и модуль обхода DPI **уже внутри** — докачивать ничего не надо.

Проверяйте контрольные суммы (рядом с каждым файлом лежит `.sha256`):

```powershell
certutil -hashfile v2crackN-<версия>-windows-10-11.zip SHA256
```

Windows SmartScreen может предупредить о неизвестном издателе: «Подробнее» → «Выполнить в любом случае».

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

Для продолжения незавершённой разработки и проверок см. [памятку для следующего агента](AGENTS.md) и [чек-лист проверки нового интерфейса](app/TESTING.md).

Требуется **.NET SDK 10**. Основной Windows-интерфейс — WPF-приложение в
[`app/`](app/UiProto.csproj), использует общую `ServiceLib`:

```powershell
dotnet build app\UiProto.csproj -c Release
dotnet run --project v2rayN\ServiceLib.Tests\ServiceLib.Tests.csproj -c Release
```

Всё решение сразу (новый UI + Avalonia + ServiceLib + тесты):

```powershell
git clone https://github.com/wifitldev/v2crackpc.git
cd v2crackpc
dotnet build v2rayN\v2rayN.sln -c Release
```

| Проект | Назначение |
| --- | --- |
| `app\` | **Основной** WPF-интерфейс Windows-клиента |
| `v2rayN\v2rayN.Desktop` | Avalonia — кроссплатформенный (Linux / macOS) |
| `v2rayN\ServiceLib` | Общая логика: конфиги, подписки, ядро, системный прокси |
| `tools\installer` | WinForms-установщик для готовых сборок |
| `legacy\` | Архив старого WPF-UI и его упаковщика — не собирается в релизы |

Для упаковки релиза есть [`tools/pack-ui-proto.ps1`](tools/pack-ui-proto.ps1).
Перед запуском прочитайте скрипт: он очищает локальные логи и временные файлы
в staging-каталогах публикации.

```powershell
.\tools\pack-ui-proto.ps1 -Version 1.2.3 -CoreSource "C:\cores" -SingBoxExe "C:\cores\sing-box.exe"
# При необходимости добавить -Installer
```

В `CoreSource` нужны `xray.exe`, `geoip.dat`, `geosite.dat` и `wintun.dll`;
`sing-box.exe` обязателен и указывается отдельно. Скрипт формирует ZIP
`v2crackN-<версия>-windows-10-11.zip` и файл SHA-256 в `dist`, а при `-Installer` —
также установщик и его контрольную сумму. Перед публикацией проверьте архив на
тестовой машине, состав файлов и хеш. Проверка на личные файлы в скрипте полезна,
но не заменяет ручной ревизии: журналы и конфигурации могут содержать адреса
и учётные данные.

Сценарии проверки подключения, трея и релизного архива, включая меры безопасности,
описаны в [чек-листе тестирования](app/TESTING.md).

> [!NOTE]
> В исходниках **нет бинарных ядер** — как и в апстриме. Для готового релиза
> ядра подкладываются скриптом упаковки (см. ниже) или скачиваются в меню
> **Help → Check Update**.

---

## Как выпустить новую версию

```powershell
# 1. собрать самодостаточный архив с ядрами (и установщик при -Installer)
powershell -File tools\pack-ui-proto.ps1 -Version 1.2.3 -CoreSource "C:\cores" -SingBoxExe "C:\cores\sing-box.exe"
# 2. закоммитить и запушить
git add -A
git commit -m "release 1.2.3"
git push

# 3. GitHub → Releases → Create a new release
#    тег:      1.2.3
#    ассеты:   dist\v2crackN-1.2.3-windows-10-11.zip (+ .sha256)
#              dist\v2crackN-1.2.3-windows-10-11-installer.exe (+ .sha256)
```

Архивы Linux/macOS собирает CI (`.github/workflows`), имена ассетов
`v2crackN-<платформа>.zip` должны совпадать с `Global.AppReleaseAssetName`.

Как только релиз опубликован, клиент сам подхватит его:
**Help → Check Update** сверяет тег релиза с текущей версией.

---

## Структура репозитория

```
v2crackpc/
├── app/                     # основной WPF-интерфейс Windows-клиента + TESTING.md
├── v2rayN/
│   ├── ServiceLib/          # общая логика: модели, хендлеры, вью-модели, сервисы
│   ├── ServiceLib.Tests/    # TUnit-тесты (запуск: dotnet run, не dotnet test)
│   ├── v2rayN.Desktop/      # Avalonia-приложение (Linux/macOS)
│   └── v2rayN.sln           # решение: app + Avalonia + ServiceLib + тесты
├── tools/
│   ├── pack-ui-proto.ps1    # упаковка релиза Windows (ZIP + установщик)
│   ├── installer/           # WinForms-установщик готовой сборки
│   ├── dpi/ciadpi.exe       # модуль обхода DPI (byedpi)
│   └── IconGen/             # генератор иконок (dotnet run --project tools\IconGen)
├── legacy/                  # архив старого WPF-UI и его упаковщика (не собирается)
├── _icon/                   # исходник иконки
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
