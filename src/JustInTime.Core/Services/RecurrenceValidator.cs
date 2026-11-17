using JustInTime.Core.Models;

namespace JustInTime.Core.Services;

/// <summary>Проверка правила повтора. Правило с пустыми DayOfMonth/TimeOfDayMinutes считается неполным (ввод пользователя).</summary>
public static class RecurrenceValidator
{
    /// <summary>Ошибки правила; для готового к расчёту правила (Complete) время суток и число месяца обязательны.</summary>
    public static IReadOnlyList<string> Validate(RecurrenceRule? rule, bool requireComplete = true)
    {
        var errors = new List<string>();

        if (rule is null)
        {
            errors.Add("Не указано правило повтора.");
            return errors;
        }

        switch (rule.Kind)
        {
            case RecurrenceKind.Interval:
                if (rule.Every is < RecurrenceRule.MinIntervalMinutes or > RecurrenceRule.MaxIntervalMinutes)
                {
                    errors.Add(
                        $"Интервал должен быть от {RecurrenceRule.MinIntervalMinutes} минут до {RecurrenceRule.MaxIntervalMinutes} минут (7 суток).");
                }

                break;

            case RecurrenceKind.Daily:
                if (rule.Every is < 1 or > RecurrenceRule.MaxEveryDays)
                {
                    errors.Add($"Повтор каждые N дней: N от 1 до {RecurrenceRule.MaxEveryDays}.");
                }

                break;

            case RecurrenceKind.Weekly:
                if (rule.DaysOfWeekMask is <= 0 or > 0b1111111)
                {
                    errors.Add("Выберите хотя бы один день недели.");
                }

                break;

            case RecurrenceKind.Monthly:
                if (rule.Every is < 1 or > RecurrenceRule.MaxEveryMonths)
                {
                    errors.Add($"Повтор каждые N месяцев: N от 1 до {RecurrenceRule.MaxEveryMonths}.");
                }

                if (rule.DayOfMonth is { } day && day is < 1 or > 31)
                {
                    errors.Add("Число месяца должно быть от 1 до 31.");
                }

                if (requireComplete && rule.DayOfMonth is null)
                {
                    errors.Add("Не указано число месяца.");
                }

                break;

            default:
                errors.Add("Неизвестный вид повтора.");
                break;
        }

        if (requireComplete
            && rule.Kind != RecurrenceKind.Interval
            && rule.TimeOfDayMinutes is not (>= 0 and < 24 * 60))
        {
            errors.Add("Не указано время суток для повтора.");
        }

        if (rule.TimeOfDayMinutes is { } t && t is < 0 or >= 24 * 60)
        {
            errors.Add("Время суток для повтора вне допустимых границ.");
        }

        return errors;
    }
}
