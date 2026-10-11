# Фактична перевірка для capstone

Дата прогону: **2026-10-11**. Результат: **успішно, код завершення 0**.

## Середовище

- Windows.
- .NET SDK **10.0.401**.
- Windows PowerShell **5.1.26100.9444**.
- Поточна гілка: `codex/analytics-et-iterations`.

## Відтворення

Запустіть з кореня capstone-репозиторію у Windows PowerShell 5.1:

```powershell
powershell.exe -NoProfile -File .\submissions\sergiilutai\scripts\verify.ps1
$LASTEXITCODE
```

Потрібен .NET 10 SDK. Перший запуск відновлює пакети через NuGet і потребує
відповідного мережевого доступу. Під час зафіксованого прогону всі залежності
вже були відновлені. Облікові дані Partner Center не потрібні.

[Скрипт verify.ps1](../../scripts/verify.ps1) послідовно виконав:

```text
dotnet build AnalyticsET.slnx --configuration Release
dotnet test AnalyticsET.slnx --configuration Release --no-build --filter TestCategory!=Live
dotnet format AnalyticsET.slnx --verify-no-changes --no-restore
```

Він зупиняється з ненульовим кодом, якщо будь-яка перевірка не проходить.

## Скорочений фактичний вивід

```text
All projects are up-to-date for restore.
Build succeeded.
    0 Warning(s)
    0 Error(s)

Passed!  - Failed:     0, Passed:   236, Skipped:     0, Total:   236, Duration: 28 s - AnalyticsET.Tests.dll (net10.0)
Offline verification passed. Microsoft authorization and live reports remain unverified.
```

Код завершення процесу: **0**. Перевірка форматування завершилася успішно;
`--verify-no-changes` не застосовує правок. Рядки з локальними шляхами та
несуттєві проміжні повідомлення не включені до цього скороченого виводу.

## Ідентичність коду

Після прогону всі **92 файли** відповідали збереженим SHA-256 у
[маніфесті report-path checkpoint](store-downloads-hub-report-path-source-hashes.json).
SHA-256 виконаної тестової збірки також відповідав маніфесту:

```text
d1ae62fc18bb096e7c544657a123fec844a889a214f98ea16da2f257ba4c6892
```

Це перевірка робочої копії, а не доказ опублікованого GitHub Release.
Не слід переносити цей результат на іншу версію коду без нового прогону.

## Межі результату

Прогін виключає категорію Live. Він не робив реальних запитів до Microsoft
і не використовував справжні облікові дані Partner Center; HTTP- та
credential-сценарії використовують тестові підміни. Успішні офлайн-тести
не приймають v0.2 та не доводять відповідність реального експорту звітам
Partner Center. Live-докази й незавершені критерії описані у
[стані проєкту](../project-status.md).
