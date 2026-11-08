namespace JustInTime.Core.Models;

/// <summary>Как пользователь задаёт время выполнения.</summary>
public abstract record TaskSchedule;

/// <summary>Выполнить через указанное число минут от текущего момента.</summary>
public sealed record AfterDelaySchedule(int Minutes) : TaskSchedule;

/// <summary>
/// Выполнить в указанное местное время. Если часовой пояс не задан, берётся пояс устройства.
/// LocalTime трактуется как время в этом поясе, независимо от значения DateTime.Kind.
/// </summary>
public sealed record AtLocalTimeSchedule(DateTime LocalTime, string? TimeZoneId = null) : TaskSchedule;
