using JustInTime.Core.Interfaces;
using JustInTime.Core.Localization;
using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.Core.Cli;

/// <summary>Консоль для командного режима. Вынесена в интерфейс, чтобы логика проверялась без настоящей консоли.</summary>
public interface ICliConsole
{
    /// <summary>Можно ли задать вопрос пользователю (есть консоль и ввод не перенаправлен).</summary>
    bool CanPrompt { get; }

    void WriteLine(string text);

    /// <summary>Вывод без перевода строки: для вопроса, на который тут же ждут ответ.</summary>
    void Write(string text);

    string? ReadLine();
}

/// <summary>Коды выхода командного режима.</summary>
public static class CliExitCode
{
    public const int Success = 0;
    public const int Error = 1;
    public const int InvalidCommand = 2;
    public const int NotConfirmed = 3;
}

/// <summary>
/// Выполняет команды --list, --cancel, --cancel-all, --clear-history. Опасные операции спрашивают подтверждение,
/// если не указан --force / -y; если спросить некого (нет консоли), операция не выполняется, а не «молча» подтверждается.
/// </summary>
public sealed class CliRunner(ITaskService tasks, IMaintenanceService maintenance, ICliConsole console)
{
    public async Task<int> RunAsync(CliRequest request, CancellationToken cancellationToken = default)
    {
        switch (request.Command)
        {
            case CliCommand.Help:
            case CliCommand.Invalid:
                return ShowUsage(request, console);

            case CliCommand.List:
                return await ListAsync(request.IncludeAll, cancellationToken);

            case CliCommand.Cancel:
            {
                var result = await tasks.CancelAsync(request.TaskId!.Value, cancellationToken);

                if (!result.Succeeded)
                {
                    console.WriteLine(result.ErrorText);
                    return CliExitCode.Error;
                }

                console.WriteLine(Loc.T("cli_cancel_ok"));

                if (result.Warnings.Count > 0)
                {
                    console.WriteLine(result.WarningText);
                }

                return CliExitCode.Success;
            }

            case CliCommand.CancelAll:
                return await CancelAllAsync(request.Force, cancellationToken);

            case CliCommand.ClearHistory:
                return await ClearHistoryAsync(request.Force, cancellationToken);

            default:
                return CliExitCode.InvalidCommand;
        }
    }

    /// <summary>Справка или сообщение о неверной команде. Не требует базы данных, поэтому доступна статически.</summary>
    public static int ShowUsage(CliRequest request, ICliConsole console)
    {
        if (request.Command == CliCommand.Invalid)
        {
            console.WriteLine(request.Error ?? Loc.T("err_unknown"));
            console.WriteLine(Loc.T("cli_usage"));
            return CliExitCode.InvalidCommand;
        }

        console.WriteLine(Loc.T("cli_usage"));
        return CliExitCode.Success;
    }

    private async Task<int> ListAsync(bool includeAll, CancellationToken cancellationToken)
    {
        var statuses = includeAll
            ? null
            : ScheduledTaskStatuses.Active.Append(ScheduledTaskStatus.Failed).ToArray();
        var list = await tasks.GetListAsync(statuses, cancellationToken);

        if (list.Count == 0)
        {
            console.WriteLine(Loc.T("cli_list_empty"));
            return CliExitCode.Success;
        }

        foreach (var task in list)
        {
            console.WriteLine(Loc.T(
                "cli_list_row",
                task.Id.ToString("D"),
                task.ScheduledAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                task.Status.ToDisplayName(),
                task.Title));
        }

        return CliExitCode.Success;
    }

    private async Task<int> CancelAllAsync(bool force, CancellationToken cancellationToken)
    {
        var count = await maintenance.CountCancellableAsync(cancellationToken);

        if (count == 0)
        {
            console.WriteLine(Loc.T("tasks_cancel_all_none"));
            return CliExitCode.Success;
        }

        var denied = Confirm(DangerAssessor.ForCancelMany(count), force);

        if (denied is not null)
        {
            return denied.Value;
        }

        var result = await maintenance.CancelAllAsync(cancellationToken);

        console.WriteLine(result.Failed == 0
            ? Loc.T("tasks_cancel_all_done", result.Done)
            : Loc.T("tasks_cancel_all_partial", result.Done, result.Failed, string.Join(" ", result.Problems)));

        return result.Failed == 0 ? CliExitCode.Success : CliExitCode.Error;
    }

    private async Task<int> ClearHistoryAsync(bool force, CancellationToken cancellationToken)
    {
        var count = await maintenance.CountFinishedHistoryAsync(cancellationToken);

        if (count == 0)
        {
            console.WriteLine(Loc.T("hist_clear_none"));
            return CliExitCode.Success;
        }

        var denied = Confirm(DangerAssessor.ForClearHistory(count), force);

        if (denied is not null)
        {
            return denied.Value;
        }

        console.WriteLine(Loc.T("hist_clear_done", await maintenance.ClearHistoryAsync(cancellationToken)));
        return CliExitCode.Success;
    }

    /// <summary>null, если можно продолжать; иначе код выхода (подтверждения нет).</summary>
    private int? Confirm(IReadOnlyList<DangerReason> reasons, bool force)
    {
        if (!ConfirmationPolicy.NeedsConfirmation(reasons, force))
        {
            return null;
        }

        if (!console.CanPrompt)
        {
            console.WriteLine(Loc.T("cli_need_confirm"));
            return CliExitCode.NotConfirmed;
        }

        console.Write(Loc.T("cli_prompt", string.Join(" ", reasons.Select(r => r.Text))));

        if (IsYes(console.ReadLine()))
        {
            return null;
        }

        console.WriteLine(Loc.T("cli_declined"));
        return CliExitCode.NotConfirmed;
    }

    public static bool IsYes(string? answer) =>
        answer?.Trim().ToLowerInvariant() is "y" or "yes" or "д" or "да";
}
