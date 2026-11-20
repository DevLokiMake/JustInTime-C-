# Обязательные сценарии тестирования и где они проверяются

Все тесты: `dotnet test`. Тесты с PostgreSQL используют отдельную базу `justintime_test`.
Автоматические тесты работают с поддельными окнами и приложениями. Реальные WhatsApp и Telegram проверяются вручную (см. README).

| № | Сценарий | Где проверяется |
|---|----------|-----------------|
| 1 | Создание задачи | `TaskServiceTests.Create_saves_task_with_all_fields`, `Create_with_invalid_input_...`, `TaskValidatorTests` |
| 2 | Сохранение задачи | `TaskRepositoryTests`, `TaskServiceTests` (PostgreSQL) |
| 3 | Выполнение через 15 минут | `TaskServiceTests.Task_created_for_15_minutes_...`, `ScheduleResolverTests.After_delay_...`, `SchedulerEngineTests.Due_task_is_executed_and_completed` |
| 4 | Отмена задачи | `TaskServiceTests.Cancel_...`, `SchedulerEngineTests.Cancelled_task_is_not_executed` |
| 5 | Изменение времени | `TaskServiceTests.Update_changes_time_...`, `Update_registers_new_time` |
| 6 | Перезагрузка / перезапуск | `SchedulerEngineTests.Reconcile_...`, `Task_stuck_in_running_...`, `RecoverStuck_...`, `RecurrenceEngineTests` |
| 7 | Нет интернета | `MessagingTests.No_network_waits_and_adapter_is_not_called` |
| 8 | Нет разрешений | Частично: `SystemSettingExecutorTests.Any_unconfirmed_result_is_a_failure_...` (радио без доступа). Отдельных разрешений у Windows-версии нет, у Android появятся на этапе 7 |
| 9 | Заблокированное устройство | `SchedulerEngineTests.Desktop_task_waits_for_unlock_...`, `Desktop_task_runs_after_unlock`, `Non_desktop_task_runs_on_locked_computer` |
| 10 | Мессенджер недоступен | `TelegramDesktopAdapterTests.Browser_tab_with_telegram_...`, `Changed_interface_without_composer_...`. Для WhatsApp окно ищется до 60 с, быстрого теста нет (пробел) |
| 11 | Контакт не найден | Поиск по имени отключён намеренно: `TelegramDesktopAdapterTests.Contact_name_is_refused_...`. Для номера и @username: Telegram сам покажет ошибку в чате, это проверяется только вручную (пробел) |
| 12 | Изменился интерфейс мессенджера | `Changed_interface_without_send_button_...` (оба адаптера), `Changed_interface_without_composer_...` (Telegram), `Text_that_cannot_be_verified_...` |
| 13 | Повторное выполнение | `SchedulerEngineTests.Completed_task_is_not_executed_again`, `Two_engines_running_at_once_...`, `MessagingTests.Message_is_sent_once_per_execution` |
| 14 | Неопределённый результат отправки | `MessagingTests.Uncertain_becomes_unknown_...`, `SchedulerEngineTests.Unknown_result_is_never_retried_automatically`, `ResolveUnknownTests`, `Click_without_cleared_field_is_uncertain_...` |

Известные пробелы: быстрые тесты на «окно WhatsApp не найдено» (мешает фиксированный тайм-аут 60 с), проверка настоящих мессенджеров и перезагрузки Windows (только вручную).
