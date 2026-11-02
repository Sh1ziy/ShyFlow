\# Dynamic Island — Windows 11 (WPF + .NET 10)



\## Стек

\- C# / .NET 10

\- WPF

\- Цель: production-ready Dynamic Island



\## Команды

\- Сборка: `dotnet build`

\- Запуск: `dotnet run --project <имя\_проекта>`

\- Тесты: `dotnet test`



\## Правила

\- Только точечные правки, не переписывай всё

\- Возвращай diff/patch когда возможно

\- Nullable reference types включены

\- async/await только, никаких .Result / .Wait()

\- Idle CPU/RAM — приоритет №1

\- Fullscreen hide, multi-monitor, DPI scaling обязательны



\## Структура (заполни под свой проект)

\- Views/ — окна и UserControls

\- ViewModels/

\- Services/ — media, notifications, system stats

\- Helpers/

