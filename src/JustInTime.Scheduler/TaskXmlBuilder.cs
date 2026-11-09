using System.Globalization;
using System.Xml.Linq;

namespace JustInTime.Scheduler;

/// <summary>
/// Собирает XML-описание задания для Планировщика заданий Windows (схема Task Scheduler 1.4, Windows 7 и новее).
/// XML строится через XDocument, поэтому спецсимволы в путях экранируются автоматически.
/// </summary>
public static class TaskXmlBuilder
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <param name="start">
    /// Момент запуска. Передаётся вместе со смещением от UTC, поэтому Планировщик запускает задание
    /// в тот же абсолютный момент, даже если часовой пояс компьютера потом сменится.
    /// </param>
    public static string Build(
        DateTimeOffset start,
        string executablePath,
        string arguments,
        string workingDirectory,
        string description,
        TimeSpan executionTimeLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var exec = new XElement(Ns + "Exec",
            new XElement(Ns + "Command", executablePath),
            new XElement(Ns + "Arguments", arguments));

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            exec.Add(new XElement(Ns + "WorkingDirectory", workingDirectory));
        }

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(Ns + "Task",
                new XAttribute("version", "1.4"),
                new XElement(Ns + "RegistrationInfo",
                    new XElement(Ns + "Author", "JustInTime"),
                    new XElement(Ns + "Description", description)),
                new XElement(Ns + "Triggers",
                    new XElement(Ns + "TimeTrigger",
                        new XElement(Ns + "StartBoundary", FormatStart(start)),
                        new XElement(Ns + "Enabled", "true"))),
                // Без UserId и пароля: задание запускается от имени создавшего его пользователя
                // и только пока он вошёл в систему. Это нужно для работы с интерфейсом (сессия не 0).
                new XElement(Ns + "Principals",
                    new XElement(Ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(Ns + "LogonType", "InteractiveToken"),
                        new XElement(Ns + "RunLevel", "LeastPrivilege"))),
                new XElement(Ns + "Settings",
                    new XElement(Ns + "MultipleInstancesPolicy", "Parallel"),
                    new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(Ns + "AllowHardTerminate", "true"),
                    // Если в момент запуска компьютер был выключен или спал, задание выполнится при первой возможности.
                    new XElement(Ns + "StartWhenAvailable", "true"),
                    new XElement(Ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(Ns + "IdleSettings",
                        new XElement(Ns + "StopOnIdleEnd", "false"),
                        new XElement(Ns + "RestartOnIdle", "false")),
                    new XElement(Ns + "AllowStartOnDemand", "true"),
                    new XElement(Ns + "Enabled", "true"),
                    new XElement(Ns + "Hidden", "false"),
                    new XElement(Ns + "RunOnlyIfIdle", "false"),
                    new XElement(Ns + "WakeToRun", "false"),
                    new XElement(Ns + "ExecutionTimeLimit", FormatDuration(executionTimeLimit)),
                    new XElement(Ns + "Priority", "7")),
                new XElement(Ns + "Actions",
                    new XAttribute("Context", "Author"),
                    exec)));

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        document.Save(writer);
        return writer.ToString();
    }

    /// <summary>xs:dateTime со смещением, например 2026-11-08T21:30:00+05:00.</summary>
    public static string FormatStart(DateTimeOffset start) =>
        start.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>xs:duration, например PT15M.</summary>
    public static string FormatDuration(TimeSpan value) =>
        System.Xml.XmlConvert.ToString(value);
}
