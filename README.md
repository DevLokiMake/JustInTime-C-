# JustInTime (Windows)

Приложение для планирования и автоматического выполнения действий на Windows.
Сейчас готов этап 1: основа решения, подключение к PostgreSQL, Dashboard и навигация.

Стек: C#, .NET 10, WPF, MVVM (CommunityToolkit.Mvvm), PostgreSQL, EF Core + Npgsql, Serilog.

## Структура

```
src/
  JustInTime.Core            модели, интерфейсы, DashboardService (ни от кого не зависит)
  JustInTime.Infrastructure  EF Core, DbContext, репозитории, подключение к PostgreSQL
  JustInTime.App             WPF: окна, ViewModels, навигация, DI, Serilog
  JustInTime.Scheduler       каркас (этап 4)
  JustInTime.Automation      каркас (этап 6)
  JustInTime.Messaging       каркас (этапы 8-9)
  JustInTime.SystemControl   каркас (этап 10)
```

## Первый запуск

Нужно: .NET SDK 10, запущенный PostgreSQL на localhost:5432.
Все команды выполняются в PowerShell из папки с `JustInTime.sln`.

1. Инструмент миграций (один раз):

```powershell
dotnet tool install --global dotnet-ef
```

2. Пароль PostgreSQL кладём в user-secrets. В Git он не попадает. Подставь свой пароль:

```powershell
dotnet user-secrets set "Database:Password" "ТВОЙ_ПАРОЛЬ" --project src/JustInTime.App
```

Хост, порт, имя базы и пользователь лежат в `src/JustInTime.App/appsettings.json`
(по умолчанию `localhost`, `5432`, `justintime_dev`, `postgres`).

3. Первая миграция и создание базы:

```powershell
dotnet ef migrations add InitialCreate --project src/JustInTime.Infrastructure --startup-project src/JustInTime.App --output-dir Database/Migrations
dotnet ef database update --project src/JustInTime.Infrastructure --startup-project src/JustInTime.App
```

База `justintime_dev` создастся сама, если её нет. Таблицы: `Tasks`, `ExecutionHistory`.
Посмотреть их можно в pgAdmin 4: Databases / justintime_dev / Schemas / public / Tables.

4. Запуск:

```powershell
dotnet run --project src/JustInTime.App
```

или открой `JustInTime.sln` в Visual Studio и запусти проект JustInTime.App (F5).

## Миграции

Каждое изменение моделей это новая миграция. Пример для этапа 2:

```powershell
dotnet ef migrations add AddSettingsAndNullableResult --project src/JustInTime.Infrastructure --startup-project src/JustInTime.App --output-dir Database/Migrations
dotnet ef database update --project src/JustInTime.Infrastructure --startup-project src/JustInTime.App
```

Папку `src/JustInTime.Infrastructure/Database/Migrations` нужно хранить в Git.

## Тесты

Тесты работают на реальном PostgreSQL. Они сами создают и пересоздают базу `justintime_test`
(рабочая `justintime_dev` не затрагивается) и берут пароль из тех же user-secrets.
Нужны применённые миграции в проекте Infrastructure.

```powershell
dotnet test
```

## Логи

`%LocalAppData%\JustInTime\logs\justintime-YYYYMMDD.log`, хранятся 14 дней.

## Что работает на этапе 1

- Окно с тёмной темой, боковая навигация по 7 разделам.
- Dashboard читает реальные данные из PostgreSQL: активные задачи, ближайшая задача,
  задачи с ошибкой, последние действия и ошибки. Пока таблицы пустые, показываются пустые состояния.
- При старте автоматически применяются миграции. Если PostgreSQL недоступен, приложение
  показывает точную ошибку и не падает.

## Что добавлено на этапе 2

- Таблица `Settings` и `ISettingsStore` (атомарная запись ключа, помощники для bool и int).
- Атомарные операции над задачами в БД: запуск (`TryStartExecutionAsync`), завершение попытки,
  отмена, изменение с перепланированием. Условие и смена статуса выполняются одним запросом.
- Защита от двойного запуска: одну задачу может взять только один исполнитель.
- Задача со статусом `NeedsUserAction` (неизвестный результат) не запускается автоматически,
  чтобы не создать дубль сообщения.
- `ExecutionHistory.Result` теперь nullable: пока попытка идёт, результата ещё нет.
- Тесты репозиториев на реальном PostgreSQL (xUnit).

## Что пока не реализовано

- Остальные разделы (My Tasks, Create Task и т.д.): открываются как страницы-заготовки.
- Сервис задач с валидацией и экраны создания и редактирования (этап 3).
- Планировщик, уведомления.
- Мессенджеры, UI Automation, управление системой.
- Тесты слоёв выше репозиториев (их пока нет, потому что нет самих слоёв).
- Модули Scheduler, Automation, Messaging, SystemControl содержат только файл проекта.
