using JustInTime.Core.Models;
using JustInTime.Core.Localization;

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
            errors.Add(Loc.T("val_rec_missing"));
            return errors;
        }

        switch (rule.Kind)
        {
            case RecurrenceKind.Interval:
                if (rule.Every is < RecurrenceRule.MinIntervalMinutes or > RecurrenceRule.MaxIntervalMinutes)
                {
                    errors.Add(
                        Loc.T("val_rec_interval", RecurrenceRule.MinIntervalMinutes, RecurrenceRule.MaxIntervalMinutes));
                }

                break;

            case RecurrenceKind.Daily:
                if (rule.Every is < 1 or > RecurrenceRule.MaxEveryDays)
                {
                    errors.Add(Loc.T("val_rec_days", RecurrenceRule.MaxEveryDays));
                }

                break;

            case RecurrenceKind.Weekly:
                if (rule.DaysOfWeekMask is <= 0 or > 0b1111111)
                {
                    errors.Add(Loc.T("val_rec_weekday"));
                }

                break;

            case RecurrenceKind.Monthly:
                if (rule.Every is < 1 or > RecurrenceRule.MaxEveryMonths)
                {
                    errors.Add(Loc.T("val_rec_months", RecurrenceRule.MaxEveryMonths));
                }

                if (rule.DayOfMonth is { } day && day is < 1 or > 31)
                {
                    errors.Add(Loc.T("val_rec_day_range"));
                }

                if (requireComplete && rule.DayOfMonth is null)
                {
                    errors.Add(Loc.T("val_rec_day_missing"));
                }

                break;

            default:
                errors.Add(Loc.T("val_rec_kind"));
                break;
        }

        if (requireComplete
            && rule.Kind != RecurrenceKind.Interval
            && rule.TimeOfDayMinutes is not (>= 0 and < 24 * 60))
        {
            errors.Add(Loc.T("val_rec_time_missing"));
        }

        if (rule.TimeOfDayMinutes is { } t && t is < 0 or >= 24 * 60)
        {
            errors.Add(Loc.T("val_rec_time_range"));
        }

        return errors;
    }
}
