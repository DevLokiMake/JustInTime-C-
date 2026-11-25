namespace JustInTime.Core.Cli;

public enum CliCommand
{
    /// <summary>Командного режима нет: обычный запуск окна.</summary>
    None,
    Help,
    List,
    Cancel,
    CancelAll,
    ClearHistory,
    /// <summary>Команда указана неверно, причина в Error.</summary>
    Invalid
}

public sealed record CliRequest(
    CliCommand Command, bool Force, bool IncludeAll = false, Guid? TaskId = null, string? Error = null);

/// <summary>Разбор аргументов командной строки. Ключи --run-due, --background и --dump-ui здесь не разбираются.</summary>
public static class CliParser
{
    public static readonly string[] ForceFlags = ["--force", "-y"];

    public static CliRequest Parse(IReadOnlyList<string> args)
    {
        var force = args.Any(a => ForceFlags.Contains(a, StringComparer.OrdinalIgnoreCase));
        var includeAll = args.Any(a => string.Equals(a, "--all", StringComparison.OrdinalIgnoreCase));

        var commands = new List<(CliCommand Command, int Index)>();

        for (var i = 0; i < args.Count; i++)
        {
            var command = args[i].ToLowerInvariant() switch
            {
                "--help" or "-h" or "/?" => CliCommand.Help,
                "--list" => CliCommand.List,
                "--cancel" => CliCommand.Cancel,
                "--cancel-all" => CliCommand.CancelAll,
                "--clear-history" => CliCommand.ClearHistory,
                _ => CliCommand.None
            };

            if (command != CliCommand.None)
            {
                commands.Add((command, i));
            }
        }

        if (commands.Count == 0)
        {
            return new CliRequest(CliCommand.None, force);
        }

        if (commands.Count > 1)
        {
            return new CliRequest(CliCommand.Invalid, force, Error: Localization.Loc.T("cli_err_conflict"));
        }

        var (found, index) = commands[0];

        if (found != CliCommand.Cancel)
        {
            return new CliRequest(found, force, includeAll);
        }

        if (index + 1 < args.Count && Guid.TryParse(args[index + 1], out var id))
        {
            return new CliRequest(CliCommand.Cancel, force, TaskId: id);
        }

        return new CliRequest(CliCommand.Invalid, force, Error: Localization.Loc.T("cli_err_cancel_id"));
    }
}
