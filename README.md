# KROT zapret

Windows-приложение для автоматической настройки Zapret под отдельные интернет-сервисы.

KROT работает локально, не является VPN, не использует собственные серверы и не устанавливает системный прокси. Обработка ограничивается выбранными сервисами и выполняется готовыми компонентами `winws`/`winws2` и WinDivert.

## Состояние проекта

Текущая версия — ранний рабочий каркас `v0.1.0-alpha`:

- компактный WPF-интерфейс и системный трей;
- русский и английский языки;
- выбор Discord, YouTube и нейросетей;
- явная модель состояний;
- Windows Service и защищённый Named Pipe;
- отдельные роли `main` и `voice`;
- атомарные JSON-настройки;
- пользовательский журнал и ротируемые технические логи;
- локальный Debug runtime на Zapret v72.13 и Zapret 2 v1.0.3;
- проверка runtime-файлов по SHA-256 и владение процессами через Job Object;
- точечные профили Discord и YouTube;
- unit- и integration-подобные тесты менеджера процессов и профилей;
- заготовка установщика Inno Setup.

Публичная alpha-сборка пока остаётся на fake runtime. В локальной Debug-сборке реальный runtime включается отдельной Windows-службой и используется для полевых тестов контролируемых пресетов.

## Требования

- Windows 10 22H2 x64 или Windows 11 x64;
- Visual Studio 2022 с .NET Framework 4.8 Developer Pack либо современный .NET SDK;
- Inno Setup 6 — только для сборки установщика.

Пользователю готового установщика отдельно устанавливать зависимости не потребуется.

## Сборка

```powershell
dotnet restore KROT.sln
dotnet build KROT.sln -c Release -p:Platform=x64
dotnet test KROT.sln -c Release -p:Platform=x64 --no-build
```

Либо:

```powershell
.\scripts\build.ps1
```

Скрипт по умолчанию собирает локальную Debug-конфигурацию. Для явного выбора:

```powershell
.\scripts\build.ps1 -Configuration Debug
.\scripts\build.ps1 -Configuration Release
```

Автономная проверка соединения без VPN описана в
[docs/FIELD_TESTING.md](docs/FIELD_TESTING.md).

Для локальной проверки реального runtime нужно установить и запустить Debug-службу:

```powershell
.\scripts\install-debug-service.ps1
```

Удаление службы:

```powershell
.\scripts\uninstall-debug-service.ps1
```

Основное Debug-приложение после сборки:

```text
src\KROT.App\bin\x64\Debug\net48\KROT.exe
```

## Структура

```text
src/
  KROT.App/             WPF-интерфейс
  KROT.Service/         привилегированная Windows Service
  KROT.Core/            модели, состояния и контракты
  KROT.Infrastructure/  настройки, сеть и логи
  KROT.Diagnostics/     диагностика сервисов
  KROT.Zapret/          runtime и формирование аргументов
  KROT.Localization/    RU/EN

tests/                  автоматические тесты
installer/              Inno Setup
third_party/            изолированные версии Zapret и лицензии
assets/                 заменяемые векторные ресурсы
```

Подробности архитектуры находятся в [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Безопасность и приватность

KROT не читает токены, cookies, сообщения, Discord-логи и содержимое HTTPS. Приложение не завершает чужие процессы `winws` и не вмешивается в сторонние VPN, прокси или сетевые инструменты. Подробные логи не должны содержать персональные данные и пользовательский трафик.

## Сторонние компоненты

Версии, источники и SHA-256 перечислены в `third_party/zapret/runtime-manifest.json`. Лицензии и уведомления находятся в [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).

## Лицензия

Код KROT распространяется по лицензии MIT. Сторонние компоненты сохраняют собственные лицензии.
